using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NekoProxyCore.Core;

/// <summary>Immutable RSA public key used only for RS256 permit verification.</summary>
public sealed class RsaTrustedPublicKey : ITrustedPublicKey, IDisposable
{
    private readonly RSA _rsa;

    private RsaTrustedPublicKey(RSA rsa) => _rsa = rsa;

    public static RsaTrustedPublicKey FromParameters(RSAParameters parameters)
    {
        if (parameters.Modulus is null || parameters.Exponent is null ||
            parameters.D is not null || parameters.P is not null || parameters.Q is not null ||
            parameters.DP is not null || parameters.DQ is not null || parameters.InverseQ is not null)
        {
            throw new ArgumentException("Public RSA parameters are required.", nameof(parameters));
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportParameters(parameters);
            return new RsaTrustedPublicKey(rsa);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    internal bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        lock (_rsa)
            return _rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public void Dispose() => _rsa.Dispose();
}

/// <summary>Exact, immutable key-id allow-list. It never falls back to another key.</summary>
public sealed class ImmutableTrustedPublicKeyResolver : ITrustedPublicKeyResolver
{
    private readonly IReadOnlyDictionary<string, ITrustedPublicKey> _keys;

    public ImmutableTrustedPublicKeyResolver(IReadOnlyDictionary<string, ITrustedPublicKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = new Dictionary<string, ITrustedPublicKey>(keys, StringComparer.Ordinal);
    }

    public bool TryResolve(string keyId, out ITrustedPublicKey? key) => _keys.TryGetValue(keyId, out key);
}

public interface IPermitReplayStore
{
    bool TryConsume(string permitId, long expiresAtNumericDate);
}

/// <summary>Process-local atomic one-use JTI admission for the lifetime of the Core host.</summary>
public sealed class InMemoryPermitReplayStore : IPermitReplayStore
{
    private readonly ConcurrentDictionary<string, long> _consumed = new(StringComparer.Ordinal);

    public bool TryConsume(string permitId, long expiresAtNumericDate)
    {
        ArgumentNullException.ThrowIfNull(permitId);
        return _consumed.TryAdd(permitId, expiresAtNumericDate);
    }
}

/// <summary>Strict NEKO-AUTH-S0/s0-rc1 compact JWT RS256 verifier.</summary>
public sealed class StrictLaunchPermitVerifier : IPermitVerifier
{
    private const int MaximumPermitLength = 4096;
    private const long LifetimeSeconds = 30;
    private const long ClockSkewSeconds = 2;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> HeaderNames = new(StringComparer.Ordinal) { "alg", "typ", "kid" };
    private static readonly HashSet<string> ClaimNames = new(StringComparer.Ordinal)
    {
        "iss", "aud", "sub", "sid", "iid", "lid", "product", "scope", "cfg", "challenge",
        "target_pid", "mode", "jti", "iat", "nbf", "exp"
    };

    private readonly ITrustedPublicKeyResolver _keyResolver;
    private readonly ICanonicalConfigurationSerializer _configurationSerializer;
    private readonly ITrustedUtcClock _clock;
    private readonly IPermitReplayStore _replayStore;
    private readonly ICoreDiagnosticSink _diagnostics;
    private readonly object _clockGate = new();
    private DateTimeOffset? _lastObservedUtc;

    public StrictLaunchPermitVerifier(
        ITrustedPublicKeyResolver keyResolver,
        ICanonicalConfigurationSerializer configurationSerializer,
        ITrustedUtcClock clock,
        IPermitReplayStore replayStore)
        : this(keyResolver, configurationSerializer, clock, replayStore, null)
    {
    }

    public StrictLaunchPermitVerifier(
        ITrustedPublicKeyResolver keyResolver,
        ICanonicalConfigurationSerializer configurationSerializer,
        ITrustedUtcClock clock,
        IPermitReplayStore replayStore,
        ICoreDiagnosticSink? diagnostics)
    {
        _keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
        _configurationSerializer = configurationSerializer ?? throw new ArgumentNullException(nameof(configurationSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _replayStore = replayStore ?? throw new ArgumentNullException(nameof(replayStore));
        _diagnostics = diagnostics ?? NullCoreDiagnosticSink.Instance;
    }

    public Task<ProxyError?> VerifyAsync(
        SensitivePermit permit,
        ProxyConfiguration configuration,
        string challenge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(challenge);
        cancellationToken.ThrowIfCancellationRequested();

        var currentStage = CoreDiagnosticStage.PermitParse;
        try
        {
            return Task.FromResult(Verify(
                permit, configuration, challenge, cancellationToken, ref currentStage));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Report(currentStage, CoreDiagnosticCategory.AuthVerifierUnexpectedException);
            return Task.FromResult<ProxyError?>(Error(
                ProxyErrorCode.AuthorizationUnavailable, "Online authorization is unavailable."));
        }
    }

    private ProxyError? Verify(
        SensitivePermit permit,
        ProxyConfiguration configuration,
        string challenge,
        CancellationToken cancellationToken,
        ref CoreDiagnosticStage currentStage)
    {
        var compact = permit.Value;
        if (compact.Length is 0 or > MaximumPermitLength || compact.Any(character => character > 0x7f))
            return Invalid();

        var segments = compact.Split('.');
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty) ||
            !TryDecodeBase64Url(segments[0], out var headerBytes) ||
            !TryDecodeBase64Url(segments[1], out var payloadBytes) ||
            !TryDecodeBase64Url(segments[2], out var signatureBytes) ||
            !TryReadObject(headerBytes, HeaderNames, out var header) ||
            !TryReadObject(payloadBytes, ClaimNames, out var claims) ||
            !TryGetString(header, "alg", out var algorithm) || algorithm != "RS256" ||
            !TryGetString(header, "typ", out var type) || type != "neko-launch+jwt" ||
            !TryGetBoundedAscii(header, "kid", 128, out var keyId))
        {
            return Invalid();
        }
        ReportCompleted(CoreDiagnosticStage.PermitParse);

        cancellationToken.ThrowIfCancellationRequested();
        currentStage = CoreDiagnosticStage.KeyResolve;
        ITrustedPublicKey? trustedKey;
        try
        {
            if (!_keyResolver.TryResolve(keyId, out trustedKey))
                return Invalid();
        }
        catch
        {
            Report(currentStage, CoreDiagnosticCategory.AuthKeyResolveException);
            return Error(ProxyErrorCode.AuthorizationUnavailable, "Online authorization is unavailable.");
        }

        if (trustedKey is not RsaTrustedPublicKey rsaKey)
        {
            Report(currentStage, CoreDiagnosticCategory.AuthKeyTypeUnavailable);
            return Error(ProxyErrorCode.AuthorizationUnavailable, "Online authorization is unavailable.");
        }
        ReportCompleted(currentStage);

        currentStage = CoreDiagnosticStage.SignatureVerify;
        var signingInput = Encoding.ASCII.GetBytes(segments[0] + "." + segments[1]);
        bool signatureValid;
        try
        {
            signatureValid = rsaKey.Verify(signingInput, signatureBytes);
        }
        catch (CryptographicException)
        {
            return Invalid();
        }

        if (!signatureValid)
            return Invalid();
        ReportCompleted(currentStage);

        currentStage = CoreDiagnosticStage.ClaimsValidate;
        if (
            !HasExactString(claims, "iss", "neko-backend") ||
            !HasExactString(claims, "aud", "neko-proxy-core") ||
            !HasExactString(claims, "product", "neko-family-proxy") ||
            !HasExactString(claims, "scope", "proxy:start") ||
            !HasExactString(claims, "mode", "ProcessMode") ||
            !TryGetBoundedAscii(claims, "iss", 128, out _) ||
            !TryGetBoundedAscii(claims, "aud", 128, out _) ||
            !TryGetBoundedAscii(claims, "sub", 128, out _) ||
            !TryGetBoundedAscii(claims, "sid", 128, out _) ||
            !TryGetBoundedAscii(claims, "iid", 128, out _) ||
            !TryGetBoundedAscii(claims, "lid", 128, out _) ||
            !TryGetBoundedAscii(claims, "product", 128, out _) ||
            !TryGetBoundedAscii(claims, "scope", 128, out _) ||
            !TryGetBoundedAscii(claims, "mode", 128, out _) ||
            !TryGetBoundedAscii(claims, "jti", 64, out var permitId) ||
            !TryGetBoundedAscii(claims, "cfg", 128, out var configurationDigest) ||
            !IsLowerHexSha256(configurationDigest) ||
            !TryGetBoundedAscii(claims, "challenge", 128, out var permitChallenge) ||
            !IsChallenge(permitChallenge) ||
            !TryGetUInt32(claims, "target_pid", out var targetPid) ||
            !TryGetInt64(claims, "iat", out var issuedAt) ||
            !TryGetInt64(claims, "nbf", out var notBefore) ||
            !TryGetInt64(claims, "exp", out var expiresAt) ||
            notBefore != issuedAt ||
            issuedAt > long.MaxValue - LifetimeSeconds ||
            expiresAt != issuedAt + LifetimeSeconds)
        {
            return Invalid();
        }
        ReportCompleted(currentStage);

        currentStage = CoreDiagnosticStage.ClockValidate;
        long now;
        try
        {
            lock (_clockGate)
            {
                if (!_clock.IsTrusted)
                {
                    Report(currentStage, CoreDiagnosticCategory.AuthClockUntrusted);
                    return Error(ProxyErrorCode.AuthorizationUnavailable, "Online authorization is unavailable.");
                }

                var observedUtc = _clock.UtcNow;
                if (_lastObservedUtc is { } previousUtc && observedUtc < previousUtc)
                {
                    Report(currentStage, CoreDiagnosticCategory.AuthClockRollback);
                    return Error(ProxyErrorCode.AuthorizationUnavailable, "Online authorization is unavailable.");
                }

                _lastObservedUtc = observedUtc;
                now = observedUtc.ToUnixTimeSeconds();
            }
        }
        catch
        {
            Report(currentStage, CoreDiagnosticCategory.AuthClockException);
            return Error(ProxyErrorCode.AuthorizationUnavailable, "Online authorization is unavailable.");
        }
        ReportCompleted(currentStage);

        if (issuedAt > now + ClockSkewSeconds ||
            notBefore > now + ClockSkewSeconds ||
            expiresAt <= now - ClockSkewSeconds)
            return Error(ProxyErrorCode.AuthorizationExpired, "Online authorization expired.");

        currentStage = CoreDiagnosticStage.ConfigurationDigestValidate;
        byte[] canonicalConfiguration;
        try
        {
            canonicalConfiguration = _configurationSerializer.Serialize(configuration).ToArray();
        }
        catch (ArgumentException)
        {
            return Error(ProxyErrorCode.ConfigurationMismatch, "Proxy configuration does not match authorization.");
        }

        var actualDigest = SHA256.HashData(canonicalConfiguration);
        if (!TryDecodeLowerHex(configurationDigest, out var expectedDigest) ||
            !CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
        {
            return Error(ProxyErrorCode.ConfigurationMismatch, "Proxy configuration does not match authorization.");
        }
        ReportCompleted(currentStage);

        currentStage = CoreDiagnosticStage.TargetChallengeBind;
        if (configuration.TargetPid != targetPid ||
            !FixedTimeAsciiEquals(permitChallenge, challenge))
        {
            return Invalid();
        }
        ReportCompleted(currentStage);

        cancellationToken.ThrowIfCancellationRequested();
        currentStage = CoreDiagnosticStage.JtiConsume;
        var consumed = _replayStore.TryConsume(permitId, expiresAt);
        ReportCompleted(currentStage);
        return consumed
            ? null
            : Error(ProxyErrorCode.AuthorizationReplay, "Online authorization was already used.");
    }

    private static bool TryReadObject(
        byte[] utf8,
        HashSet<string> allowedNames,
        out Dictionary<string, JsonElement> values)
    {
        values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        try
        {
            _ = StrictUtf8.GetString(utf8);
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    return false;
                var name = reader.GetString();
                if (name is null || !allowedNames.Contains(name) || values.ContainsKey(name) || !reader.Read())
                    return false;
                using var value = JsonDocument.ParseValue(ref reader);
                values.Add(name, value.RootElement.Clone());
            }

            return reader.TokenType == JsonTokenType.EndObject && !reader.Read() &&
                   values.Count == allowedNames.Count;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryDecodeBase64Url(string text, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (text.Length == 0 || text.Contains('=') ||
            text.Any(character => !IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return false;

        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            bytes = Convert.FromBase64String(padded);
            return string.Equals(ToBase64Url(bytes), text, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryGetString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryGetBoundedAscii(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumLength,
        out string value) =>
        TryGetString(values, name, out value) &&
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value.All(character => character <= 0x7f);

    private static bool HasExactString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        string expected) =>
        TryGetString(values, name, out var value) && string.Equals(value, expected, StringComparison.Ordinal);

    private static bool TryGetInt64(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        out long value)
    {
        value = default;
        return values.TryGetValue(name, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               IsJsonInteger(element) &&
               element.TryGetInt64(out value);
    }

    private static bool TryGetUInt32(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        out uint value)
    {
        value = default;
        return values.TryGetValue(name, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               IsJsonInteger(element) &&
               element.TryGetUInt32(out value) && value > 0;
    }

    private static bool IsJsonInteger(JsonElement element)
    {
        var raw = element.GetRawText();
        if (raw.Length == 0)
            return false;

        var start = raw[0] == '-' ? 1 : 0;
        return start < raw.Length && raw[start..].All(character => character is >= '0' and <= '9');
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TryDecodeLowerHex(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!IsLowerHexSha256(value))
            return false;
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsChallenge(string value) =>
        value.Length == 43 && value.All(character =>
            IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool FixedTimeAsciiEquals(string left, string right)
    {
        if (left.Any(character => character > 0x7f) || right.Any(character => character > 0x7f))
            return false;
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static ProxyError Invalid() =>
        Error(ProxyErrorCode.AuthorizationInvalid, "Online authorization is invalid.");

    private void ReportCompleted(CoreDiagnosticStage stage) =>
        Report(stage, CoreDiagnosticCategory.StageCompleted);

    private void Report(CoreDiagnosticStage stage, CoreDiagnosticCategory category) =>
        CoreDiagnosticReporter.ReportSafely(_diagnostics, stage, category);

    private static ProxyError Error(ProxyErrorCode code, string safeMessage) => new(code, safeMessage);
}
