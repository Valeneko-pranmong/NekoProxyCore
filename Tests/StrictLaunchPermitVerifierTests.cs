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

    [TestMethod]
    public async Task ValidLitePermitWithoutRetiredS0ClaimsIsAccepted()
    {
        using var fixture = PermitFixture.Create();
        var permit = fixture.CreatePermitWithoutClaims(
            "sid", "iid", "lid", "cfg", "target_pid", "mode");

        var error = await fixture.CreateVerifier().VerifyAsync(
            permit, Challenge, CancellationToken.None);

        Assert.IsNull(error);
    }

    [TestMethod]
    public async Task MissingRequiredLiteClaimIsRejected()
    {
        using var fixture = PermitFixture.Create();
        var permit = fixture.CreatePermitWithoutClaims("scope");

        var error = await fixture.CreateVerifier().VerifyAsync(
            permit, Challenge, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task ValidS0Rc1PermitIsAcceptedExactlyOnce()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();
        var permit = fixture.CreatePermit();

        var first = await verifier.VerifyAsync(permit, Challenge, CancellationToken.None);
        var replay = await verifier.VerifyAsync(permit, Challenge, CancellationToken.None);

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
            verifier.VerifyAsync(permit, Challenge, CancellationToken.None),
            verifier.VerifyAsync(permit, Challenge, CancellationToken.None));

        Assert.AreEqual(1, Array.FindAll(results, result => result is null).Length);
        Assert.AreEqual(1, Array.FindAll(results, result => result?.Code == ProxyErrorCode.AuthorizationReplay).Length);
    }

    [TestMethod]
    public async Task RetiredConfigurationDigestDoesNotBlockLiteAuthorization()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();
        var permit = fixture.CreatePermit();

        var error = await verifier.VerifyAsync(permit, Challenge, CancellationToken.None);

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

        var unknownError = await unknown.VerifyAsync(permit, Challenge, CancellationToken.None);
        var unavailableError = await unavailable.VerifyAsync(permit, Challenge, CancellationToken.None);

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
            CancellationToken.None);
        var unknown = await verifier.VerifyAsync(
            fixture.CreatePermit(keyId: "unknown-key"),
            Challenge,
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
            fixture.CreatePermit(), Challenge, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task AlgorithmOtherThanRs256IsRejected()
    {
        using var fixture = PermitFixture.Create();

        var error = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(algorithm: "PS256"),
            Challenge,
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
            fixture.CreatePermit(), Challenge, CancellationToken.None);
        var first = await rollbackVerifier.VerifyAsync(
            fixture.CreatePermit(new() { ["jti"] = "clock-jti-1" }),
            Challenge,
            CancellationToken.None);
        rollbackClock.UtcNow = rollbackClock.UtcNow.AddSeconds(-1);
        var rollbackError = await rollbackVerifier.VerifyAsync(
            fixture.CreatePermit(new() { ["jti"] = "clock-jti-2" }),
            Challenge,
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
            fixture.CreatePermit(), Challenge, CancellationToken.None);
        var futureError = await fixture.CreateVerifier().VerifyAsync(
            futurePermit, Challenge, CancellationToken.None);

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
            permit, Challenge, CancellationToken.None);

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
            whitespaceIdentifier, Challenge, CancellationToken.None);
        var numericDateError = await fixture.CreateVerifier().VerifyAsync(
            fixture.SignRawPayload(nonIntegerPayload), Challenge, CancellationToken.None);

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
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", CancellationToken.None);
        Assert.IsTrue(SensitivePermit.TryCreate("a.b.c", 4096, out var malformed));
        var badSignature = await verifier.VerifyAsync(
            malformed!, Challenge, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, wrongChallenge!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, badSignature!.Code);
    }

    [TestMethod]
    public async Task ValidPermitReachesEngineOnlyAfterVerification()
    {
        using var fixture = PermitFixture.Create();
        var engine = new CountingEngine();
        var runtime = new HeadlessRuntimeCoordinator(
            new ProcessModeController(new ExactResolver(), engine),
            new ChallengePermitStartAuthorizer(fixture.CreateVerifier()));
        var result = await runtime.StartAsync(new ProxyStartRequest(
            fixture.Configuration,
            "0123456789abcdef0123456789abcdef",
            permit: fixture.CreatePermit(),
            admittedChallenge: Challenge));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ProxyStatusKind.Running, result.Status);
        Assert.AreEqual(1, engine.StartCount);
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

        public static PermitFixture Create() => new(RSA.Create(3072));

        public StrictLaunchPermitVerifier CreateVerifier(
            ITrustedPublicKeyResolver? resolver = null,
            ITrustedUtcClock? clock = null) =>
            new(
                resolver ?? _resolver,
                clock ?? new MutableTrustedClock(DateTimeOffset.FromUnixTimeSeconds(Now)),
                new InMemoryPermitReplayStore());

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

        private Dictionary<string, object> CreateClaims() => new()
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
            ["exp"] = 2_000_000_030
        };

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

    private sealed class CountingEngine : IProcessModeEngine
    {
        public int StartCount { get; private set; }

        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
