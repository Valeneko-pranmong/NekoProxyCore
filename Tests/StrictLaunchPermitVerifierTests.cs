using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class StrictLaunchPermitVerifierTests
{
    private const long Now = 2_000_000_001;
    private const string Kid = "neko-prod-key-2";
    private const string Challenge = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Sentinel = "SENTINEL_PROXY_SECRET_42";

    [TestMethod]
    public async Task ValidLitePermitWithoutRetiredS0ClaimsIsAccepted()
    {
        using var fixture = PermitFixture.Create();
        var permit = fixture.CreatePermitWithoutClaims(
            "sid", "iid", "lid", "cfg", "target_pid", "mode");

        var error = await fixture.CreateVerifier().VerifyAsync(
            permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.IsNull(error);
    }

    [TestMethod]
    public async Task MissingRequiredLiteClaimIsRejected()
    {
        using var fixture = PermitFixture.Create();
        var permit = fixture.CreatePermitWithoutClaims("scope");

        var error = await fixture.CreateVerifier().VerifyAsync(
            permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task ValidS0Rc1PermitIsAcceptedExactlyOnce()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();
        var permit = fixture.CreatePermit();

        var first = await verifier.VerifyAsync(permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var replay = await verifier.VerifyAsync(permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.IsNull(first);
        Assert.AreEqual(ProxyErrorCode.AuthorizationReplay, replay!.Code);
    }

    [TestMethod]
    public async Task ConcurrentReplayCanBeAcceptedAtMostOnce()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();
        var permit = fixture.CreatePermit();

        var results = await Task.WhenAll(
            verifier.VerifyAsync(permit, Challenge, fixture.RuntimeConfig, CancellationToken.None),
            verifier.VerifyAsync(permit, Challenge, fixture.RuntimeConfig, CancellationToken.None));

        Assert.AreEqual(1, Array.FindAll(results, result => result is null).Length);
        Assert.AreEqual(1, Array.FindAll(results, result => result?.Code == ProxyErrorCode.AuthorizationReplay).Length);
    }

    [TestMethod]
    public async Task RetiredConfigurationDigestDoesNotBlockLiteAuthorization()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();
        var permit = fixture.CreatePermit();

        var error = await verifier.VerifyAsync(permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.IsNull(error);
    }

    [TestMethod]
    public async Task UnknownKeyAndUnavailableResolverFailClosedDifferently()
    {
        using var fixture = PermitFixture.Create();
        var unknown = fixture.CreateVerifier(new ImmutableTrustedPublicKeyResolver(
            new Dictionary<string, ITrustedPublicKey>()));
        var unavailable = fixture.CreateVerifier(new UnavailableResolver());
        var permit = fixture.CreatePermit();

        var unknownError = await unknown.VerifyAsync(permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var unavailableError = await unavailable.VerifyAsync(permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, unknownError!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, unavailableError!.Code);
    }

    [TestMethod]
    public async Task RetiredKeyOneAndUnknownKeyIdsAreRejected()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();

        var retired = await verifier.VerifyAsync(
            fixture.CreatePermit(keyId: "neko-prod-key-1"),
            Challenge,
            fixture.RuntimeConfig,
            CancellationToken.None);
        var unknown = await verifier.VerifyAsync(
            fixture.CreatePermit(keyId: "unknown-key"),
            Challenge,
            fixture.RuntimeConfig,
            CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, retired!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, unknown!.Code);
    }

    [TestMethod]
    public async Task ModifiedPublicKeyWithCanonicalKeyIdRejectsTheSignature()
    {
        using var fixture = PermitFixture.Create();
        using var unrelated = RSA.Create(2048);
        using var unrelatedPublicKey = RsaTrustedPublicKey.FromParameters(unrelated.ExportParameters(false));
        var verifier = fixture.CreateVerifier(new ImmutableTrustedPublicKeyResolver(
            new Dictionary<string, ITrustedPublicKey> { [Kid] = unrelatedPublicKey }));

        var error = await verifier.VerifyAsync(
            fixture.CreatePermit(), Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task AlgorithmOtherThanRs256IsRejected()
    {
        using var fixture = PermitFixture.Create();

        var error = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(algorithm: "PS256"),
            Challenge,
            fixture.RuntimeConfig,
            CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task UntrustedOrRolledBackClockFailsClosed()
    {
        using var fixture = PermitFixture.Create();
        var untrusted = fixture.CreateVerifier(new MutableTrustedClock(
            DateTimeOffset.FromUnixTimeSeconds(Now), isTrusted: false));
        var rollbackClock = new MutableTrustedClock(DateTimeOffset.FromUnixTimeSeconds(Now));
        var rollbackVerifier = fixture.CreateVerifier(rollbackClock);

        var untrustedError = await untrusted.VerifyAsync(
            fixture.CreatePermit(), Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var first = await rollbackVerifier.VerifyAsync(
            fixture.CreatePermit(new() { ["jti"] = "clock-jti-1" }),
            Challenge,
            fixture.RuntimeConfig,
            CancellationToken.None);
        rollbackClock.UtcNow = rollbackClock.UtcNow.AddSeconds(-1);
        var rollbackError = await rollbackVerifier.VerifyAsync(
            fixture.CreatePermit(new() { ["jti"] = "clock-jti-2" }),
            Challenge,
            fixture.RuntimeConfig,
            CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, untrustedError!.Code);
        Assert.IsNull(first);
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, rollbackError!.Code);
    }

    [TestMethod]
    public void TrustedPublicKeyRejectsPrivateKeyMaterial()
    {
        using var signer = RSA.Create(2048);

        Assert.ThrowsException<ArgumentException>(() =>
            RsaTrustedPublicKey.FromParameters(signer.ExportParameters(true)));
    }

    [TestMethod]
    public async Task ExpiredAndFuturePermitsUseAuthorizationExpired()
    {
        using var fixture = PermitFixture.Create();
        var expired = fixture.CreateVerifier(new MutableTrustedClock(DateTimeOffset.FromUnixTimeSeconds(2_000_000_032)));
        var futurePermit = fixture.CreatePermit(claimOverrides: new()
        {
            ["iat"] = Now + 3,
            ["nbf"] = Now + 3,
            ["exp"] = Now + 33
        });

        var expiredError = await expired.VerifyAsync(
            fixture.CreatePermit(), Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var futureError = await fixture.CreateVerifier().VerifyAsync(
            futurePermit, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationExpired, expiredError!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationExpired, futureError!.Code);
    }

    [TestMethod]
    public async Task DuplicateClaimsAreRejectedBeforeSignatureCanAuthorize()
    {
        using var fixture = PermitFixture.Create();
        var payload = fixture.CreatePayloadJson();
        var duplicatePayload = payload[..^1] + ",\"jti\":\"second-jti\"}";
        var permit = fixture.SignRawPayload(duplicatePayload);

        var error = await fixture.CreateVerifier().VerifyAsync(
            permit, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task WhitespaceIdentifierAndNonIntegerNumericDateAreRejected()
    {
        using var fixture = PermitFixture.Create();
        var whitespaceIdentifier = fixture.CreatePermit(claimOverrides: new() { ["sub"] = "   " });
        var payload = fixture.CreatePayloadJson();
        var nonIntegerPayload = payload.Replace(
            "\"iat\":2000000000",
            "\"iat\":2e9",
            StringComparison.Ordinal);

        var whitespaceError = await fixture.CreateVerifier().VerifyAsync(
            whitespaceIdentifier, Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var numericDateError = await fixture.CreateVerifier().VerifyAsync(
            fixture.SignRawPayload(nonIntegerPayload), Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, whitespaceError!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, numericDateError!.Code);
    }

    [TestMethod]
    public async Task WrongChallengeAndBadSignatureAreRejected()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();
        var wrongChallenge = await verifier.VerifyAsync(
            fixture.CreatePermit(),
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", fixture.RuntimeConfig, CancellationToken.None);
        Assert.IsTrue(SensitivePermit.TryCreate("a.b.c", 4096, out var malformed));
        var badSignature = await verifier.VerifyAsync(
            malformed!, Challenge, fixture.RuntimeConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, wrongChallenge!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, badSignature!.Code);
    }

    [TestMethod]
    public async Task RuntimeConfigVersionAndCanonicalDigestAreBoundExactly()
    {
        using var fixture = PermitFixture.Create();
        Assert.IsNull(await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(), Challenge, fixture.RuntimeConfig, CancellationToken.None));

        var versionMismatch = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(new() { ["runtime_config_version"] = 19 }),
            Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var credentialMutation = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(), Challenge, fixture.CreateRuntimeConfig(credential: "SENTINEL_PROXY_SECRET_43"), CancellationToken.None);
        var hostMutation = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(), Challenge, fixture.CreateRuntimeConfig(host: "127.0.0.2"), CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, versionMismatch!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, credentialMutation!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, hostMutation!.Code);
        Assert.IsFalse((versionMismatch.SafeMessage + credentialMutation.SafeMessage + hostMutation.SafeMessage).Contains(Sentinel));
    }

    [DataTestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(9007199254740992L)]
    public async Task OutOfRangeRuntimeConfigVersionClaimIsRejected(long version)
    {
        using var fixture = PermitFixture.Create();
        var error = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(new() { ["runtime_config_version"] = version }),
            Challenge, fixture.RuntimeConfig, CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task NonIntegerRuntimeConfigVersionClaimIsRejected()
    {
        using var fixture = PermitFixture.Create();
        var payload = fixture.CreatePayloadJson().Replace(
            "\"runtime_config_version\":18", "\"runtime_config_version\":1.8e1", StringComparison.Ordinal);
        var error = await fixture.CreateVerifier().VerifyAsync(
            fixture.SignRawPayload(payload), Challenge, fixture.RuntimeConfig, CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task InvalidConfigBindingAndConfigTimeFailuresDoNotConsumeJti()
    {
        using var fixture = PermitFixture.Create();
        var replayStore = new CountingReplayStore();
        var verifier = fixture.CreateVerifier(replayStore: replayStore);
        var mismatch = await verifier.VerifyAsync(
            fixture.CreatePermit(new() { ["jti"] = "mismatch-jti", ["runtime_config_version"] = 19 }),
            Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var expiredConfig = fixture.CreateRuntimeConfig(issuedAt: Now - 123);
        var expired = await verifier.VerifyAsync(
            fixture.CreatePermitForConfig(expiredConfig, "expired-config-jti"), Challenge, expiredConfig, CancellationToken.None);
        var futureConfig = fixture.CreateRuntimeConfig(issuedAt: Now + 3);
        var future = await verifier.VerifyAsync(
            fixture.CreatePermitForConfig(futureConfig, "future-config-jti"), Challenge, futureConfig, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, mismatch!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationExpired, expired!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationExpired, future!.Code);
        Assert.AreEqual(0, replayStore.CallCount);
    }

    [TestMethod]
    public async Task RuntimeCredentialNeverAppearsInVerifierErrorsDiagnosticsOrExceptions()
    {
        using var fixture = PermitFixture.Create();
        using var writer = new System.IO.StringWriter();
        var verifier = fixture.CreateVerifier(
            replayStore: new CountingReplayStore { ThrowOnConsume = true },
            diagnostics: new SanitizedTextCoreDiagnosticSink(writer));
        var error = await verifier.VerifyAsync(
            fixture.CreatePermit(), Challenge, fixture.RuntimeConfig, CancellationToken.None);
        var exception = await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            verifier.VerifyAsync(fixture.CreatePermit(), Challenge, null!, CancellationToken.None));

        Assert.IsFalse(error!.SafeMessage.Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(writer.ToString().Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(exception.ToString().Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(fixture.RuntimeConfig.ToString().Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(fixture.RuntimeConfig.Credential.ToString().Contains(Sentinel, StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("runtime_config_version")]
    [DataRow("runtime_config_sha256")]
    public async Task MissingRuntimeConfigBindingClaimIsRejected(string claim)
    {
        using var fixture = PermitFixture.Create();
        var error = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermitWithoutClaims(claim), Challenge, fixture.RuntimeConfig, CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [DataTestMethod]
    [DataRow("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [DataRow("abcdef")]
    [DataRow("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task MalformedRuntimeConfigDigestIsRejected(string digest)
    {
        using var fixture = PermitFixture.Create();
        var error = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(new() { ["runtime_config_sha256"] = digest }),
            Challenge, fixture.RuntimeConfig, CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task ExpiredAndFutureRuntimeConfigsUseAuthorizationExpired()
    {
        using var fixture = PermitFixture.Create();
        var expired = fixture.CreateRuntimeConfig(issuedAt: Now - 123);
        var future = fixture.CreateRuntimeConfig(issuedAt: Now + 3);
        var expiredError = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermitForConfig(expired), Challenge, expired, CancellationToken.None);
        var futureError = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermitForConfig(future), Challenge, future, CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.AuthorizationExpired, expiredError!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationExpired, futureError!.Code);
    }

    [TestMethod]
    public async Task ValidPermitReachesEngineOnlyAfterVerification()
    {
        using var fixture = PermitFixture.Create();
        var engine = new CountingEngine();
        var runtime = new HeadlessRuntimeCoordinator(
            new ProcessModeController(new ExactResolver(), engine),
            new ChallengePermitStartAuthorizer(fixture.CreateVerifier()));
        var runtimeConfig = fixture.RuntimeConfig;
        var result = await runtime.StartAsync(new ProxyStartRequest(
            fixture.Configuration,
            "0123456789abcdef0123456789abcdef",
            permit: fixture.CreatePermit(),
            admittedChallenge: Challenge,
            runtimeConfig: runtimeConfig));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ProxyStatusKind.Running, result.Status);
        Assert.AreEqual(1, engine.StartCount);
        Assert.AreSame(runtimeConfig, engine.RuntimeConfig);
    }

    private sealed class PermitFixture : IDisposable
    {
        private readonly RSA _signer;
        private readonly ITrustedPublicKeyResolver _resolver;

        private PermitFixture(RSA signer)
        {
            _signer = signer;
            var publicKey = RsaTrustedPublicKey.FromParameters(signer.ExportParameters(false));
            _resolver = new ImmutableTrustedPublicKeyResolver(
                new Dictionary<string, ITrustedPublicKey> { [Kid] = publicKey });
            Configuration = new ProxyConfiguration(
                ProxyModeKind.Process, "pso2.exe", "profile-0", "server-0", targetPid: 4242);
        }

        public ProxyConfiguration Configuration { get; }
        public RuntimeProxyConfig RuntimeConfig => CreateRuntimeConfig();

        public static PermitFixture Create() => new(RSA.Create(3072));

        public StrictLaunchPermitVerifier CreateVerifier(
            ITrustedPublicKeyResolver? resolver = null,
            ITrustedUtcClock? clock = null,
            IPermitReplayStore? replayStore = null,
            ICoreDiagnosticSink? diagnostics = null) =>
            new(
                resolver ?? _resolver,
                clock ?? new MutableTrustedClock(DateTimeOffset.FromUnixTimeSeconds(Now)),
                replayStore ?? new InMemoryPermitReplayStore(),
                diagnostics);

        public StrictLaunchPermitVerifier CreateVerifier(ITrustedUtcClock clock) => CreateVerifier(null, clock);

        public SensitivePermit CreatePermit(
            Dictionary<string, object>? claimOverrides = null,
            string keyId = Kid,
            string algorithm = "RS256")
        {
            var claims = CreateClaims();
            if (claimOverrides != null)
            {
                foreach (var item in claimOverrides)
                    claims[item.Key] = item.Value;
            }

            return SignRawPayload(JsonSerializer.Serialize(claims), keyId, algorithm);
        }

        public SensitivePermit CreatePermitForConfig(RuntimeProxyConfig config, string? jti = null)
        {
            var bytes = config.CanonicalBytes();
            try
            {
                return CreatePermit(new()
                {
                    ["runtime_config_version"] = config.ConfigVersion,
                    ["runtime_config_sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    ["jti"] = jti ?? "synthetic-jti-0001"
                });
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }

        public RuntimeProxyConfig CreateRuntimeConfig(string host = "127.0.0.1", string credential = Sentinel, long issuedAt = Now - 1) =>
            new(1, 18, "japan-vps-1", host, 8389, "shadowsocks", "aes-256-gcm",
                new SensitiveRuntimeCredential(credential), issuedAt, issuedAt + 120);

        public SensitivePermit CreatePermitWithoutClaims(params string[] claimNames)
        {
            var claims = CreateClaims();
            foreach (var claimName in claimNames)
                claims.Remove(claimName);
            return SignRawPayload(JsonSerializer.Serialize(claims));
        }

        public string CreatePayloadJson() => JsonSerializer.Serialize(CreateClaims());

        public SensitivePermit SignRawPayload(
            string payload,
            string keyId = Kid,
            string algorithm = "RS256")
        {
            var header = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["alg"] = algorithm,
                ["typ"] = "neko-launch+jwt",
                ["kid"] = keyId
            });
            var signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." +
                               Base64Url(Encoding.UTF8.GetBytes(payload));
            var signature = _signer.SignData(
                Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.IsTrue(SensitivePermit.TryCreate(
                signingInput + "." + Base64Url(signature), 4096, out var permit));
            return permit!;
        }

        public void Dispose() => _signer.Dispose();

        private Dictionary<string, object> CreateClaims()
        {
            var bytes = RuntimeConfig.CanonicalBytes();
            try
            {
                return new()
                {
            ["iss"] = "neko-backend",
            ["aud"] = "neko-proxy-core",
            ["sub"] = "synthetic-subject",
            ["product"] = "neko-family-proxy",
            ["scope"] = "proxy:start",
            ["challenge"] = Challenge,
            ["jti"] = "synthetic-jti-0001",
            ["iat"] = 2_000_000_000,
            ["nbf"] = 2_000_000_000,
                    ["exp"] = 2_000_000_030,
                    ["runtime_config_version"] = RuntimeConfig.ConfigVersion,
                    ["runtime_config_sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                };
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }

        private static string Base64Url(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class MutableTrustedClock : ITrustedUtcClock
    {
        public MutableTrustedClock(DateTimeOffset now, bool isTrusted = true)
        {
            UtcNow = now;
            IsTrusted = isTrusted;
        }

        public DateTimeOffset UtcNow { get; set; }

        public bool IsTrusted { get; }
    }

    private sealed class UnavailableResolver : ITrustedPublicKeyResolver
    {
        public bool TryResolve(string keyId, out ITrustedPublicKey? key)
        {
            throw new InvalidOperationException("synthetic resolver outage");
        }
    }

    private sealed class CountingReplayStore : IPermitReplayStore
    {
        public int CallCount { get; private set; }
        public bool ThrowOnConsume { get; init; }

        public bool TryConsume(string permitId, long expiresAtNumericDate)
        {
            CallCount++;
            if (ThrowOnConsume)
                throw new InvalidOperationException(Sentinel);
            return true;
        }
    }

    private sealed class ExactResolver : IProcessResolver, IExactProcessResolver
    {
        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public Task<bool> IsExactProcessRunningAsync(
            string processName,
            uint targetPid,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task WaitForExactProcessExitAsync(
            string processName,
            uint targetPid,
            CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class CountingEngine : IProcessModeEngine, IRuntimeConfiguredProcessModeEngine
    {
        public int StartCount { get; private set; }
        public RuntimeProxyConfig? RuntimeConfig { get; private set; }

        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StartAsync(ProxyConfiguration configuration, RuntimeProxyConfig runtimeConfig, CancellationToken cancellationToken)
        {
            RuntimeConfig = runtimeConfig;
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
