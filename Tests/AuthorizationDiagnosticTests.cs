using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host;
using NekoProxyCore.Host.Protocol;

namespace Tests;

[TestClass]
public sealed class AuthorizationDiagnosticTests
{
    private const long Now = 2_000_000_001;
    private const string Challenge = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string CorrelationId = "0123456789abcdef0123456789abcdef";

    [TestMethod]
    public async Task EveryVerifierAvailabilityOriginIsTypedAndPreciselyClassified()
    {
        using var fixture = PermitFixture.Create();
        using var writer = new StringWriter();
        var diagnostics = new SanitizedTextCoreDiagnosticSink(writer);
        var permit = fixture.CreatePermit();

        await AssertUnavailableAsync(fixture.CreateVerifier(
            resolver: new ThrowingKeyResolver(), diagnostics: diagnostics), permit, fixture.Configuration);
        await AssertUnavailableAsync(fixture.CreateVerifier(
            resolver: new ForeignKeyResolver(), diagnostics: diagnostics), permit, fixture.Configuration);
        await AssertUnavailableAsync(fixture.CreateVerifier(
            clock: new FixedClock(isTrusted: false), diagnostics: diagnostics), permit, fixture.Configuration);

        var rollbackClock = new FixedClock();
        var rollbackVerifier = fixture.CreateVerifier(clock: rollbackClock, diagnostics: diagnostics);
        Assert.IsNull(await rollbackVerifier.VerifyAsync(
            fixture.CreatePermit(new() { ["jti"] = "rollback-jti-1" }),
            fixture.Configuration,
            Challenge,
            CancellationToken.None));
        // The current policy is intentionally strict: even a single 100 ns wall-clock step
        // backwards fails closed. Diagnostics distinguish this from an untrusted clock.
        rollbackClock.UtcNow = rollbackClock.UtcNow.AddTicks(-1);
        await AssertUnavailableAsync(
            rollbackVerifier,
            fixture.CreatePermit(new() { ["jti"] = "rollback-jti-2" }),
            fixture.Configuration);

        await AssertUnavailableAsync(fixture.CreateVerifier(
            clock: new ThrowingClock(), diagnostics: diagnostics), permit, fixture.Configuration);
        await AssertUnavailableAsync(fixture.CreateVerifier(
            serializer: new ThrowingSerializer(), diagnostics: diagnostics), permit, fixture.Configuration);
        await AssertUnavailableAsync(fixture.CreateVerifier(
            replayStore: new ThrowingReplayStore(), diagnostics: diagnostics), permit, fixture.Configuration);

        var output = writer.ToString();
        StringAssert.Contains(output, "stage=KEY_RESOLVE category=AUTH_KEY_RESOLVE_EXCEPTION");
        StringAssert.Contains(output, "stage=KEY_RESOLVE category=AUTH_KEY_TYPE_UNAVAILABLE");
        StringAssert.Contains(output, "stage=CLOCK_VALIDATE category=AUTH_CLOCK_UNTRUSTED");
        StringAssert.Contains(output, "stage=CLOCK_VALIDATE category=AUTH_CLOCK_ROLLBACK");
        StringAssert.Contains(output, "stage=CLOCK_VALIDATE category=AUTH_CLOCK_EXCEPTION");
        StringAssert.Contains(
            output,
            "stage=CONFIG_DIGEST_VALIDATE category=AUTH_VERIFIER_UNEXPECTED_EXCEPTION");
        StringAssert.Contains(output, "stage=JTI_CONSUME category=AUTH_VERIFIER_UNEXPECTED_EXCEPTION");
        AssertNoSensitiveMarkers(output, fixture.LastCompactPermit);
    }

    [TestMethod]
    public async Task AuthorizerRuntimeProcessAndWireTranslationOriginsRemainDistinct()
    {
        using var writer = new StringWriter();
        var diagnostics = new SanitizedTextCoreDiagnosticSink(writer);
        var configuration = CreateConfiguration();
        Assert.IsTrue(SensitivePermit.TryCreate("header.payload.signature", 4096, out var permit));

        var challengeAuthorizer = new ChallengePermitStartAuthorizer(
            new ThrowingPermitVerifier(), diagnostics);
        var authorizerError = await challengeAuthorizer.AuthorizeAsync(new ProxyStartRequest(
            configuration,
            CorrelationId,
            permit: permit,
            admittedChallenge: Challenge));
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, authorizerError!.Code);

        var rawRuntime = new HeadlessRuntimeCoordinator(
            new NoOpModeController(),
            new ThrowingStartAuthorizer(),
            null,
            null,
            diagnostics);
        var runtimeResult = await rawRuntime.StartAsync(new ProxyStartRequest(configuration));
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, runtimeResult.Error!.Code);

        var nonExactRuntime = new HeadlessRuntimeCoordinator(
            new ProcessModeController(new NameOnlyResolver(), new NoOpEngine(), diagnostics),
            new AllowStartAuthorizer(),
            null,
            null,
            diagnostics);
        var nonExactResult = await nonExactRuntime.StartAsync(new ProxyStartRequest(configuration));
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, nonExactResult.Error!.Code);

        var translated = ControlResponse.FromResult(
            ProxyResult.Failure(
                ProxyStatusKind.Failed,
                CorrelationId,
                new ProxyError(ProxyErrorCode.InvalidConfiguration, "password-marker")),
            null,
            diagnostics);
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, translated.ErrorCode);
        var missingStatusError = ControlResponse.FromStatus(
            new ProxyStatusSnapshot(
                ProxyStatusKind.Failed,
                CorrelationId,
                DateTimeOffset.UtcNow),
            CorrelationId,
            diagnostics);
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, missingStatusError.ErrorCode);

        var output = writer.ToString();
        StringAssert.Contains(output, "stage=AUTHORIZATION category=AUTHORIZER_EXCEPTION");
        StringAssert.Contains(output, "stage=AUTHORIZATION category=RUNTIME_AUTHORIZATION_UNAVAILABLE");
        StringAssert.Contains(
            output,
            "stage=PROCESS_PRECONDITION category=PROCESS_EXACT_RESOLVER_UNAVAILABLE");
        StringAssert.Contains(
            output,
            "stage=CONTROL_RESPONSE category=CONTROL_ERROR_TRANSLATED_TO_AUTHORIZATION_UNAVAILABLE");
        StringAssert.Contains(
            output,
            "stage=CONTROL_RESPONSE category=CONTROL_FAILED_STATUS_ERROR_MISSING");
        AssertNoSensitiveMarkers(output, "header.payload.signature");
    }

    [TestMethod]
    public async Task HostedShapeEphemeralRsa3072PermitAuthorizesAndReachesRuntimeStart()
    {
        using var fixture = PermitFixture.Create();
        using var writer = new StringWriter();
        var diagnostics = new SanitizedTextCoreDiagnosticSink(writer);
        var engine = new CountingEngine();
        var runtime = new HeadlessRuntimeCoordinator(
            new ProcessModeController(new ExactResolver(), engine, diagnostics),
            new ChallengePermitStartAuthorizer(fixture.CreateVerifier(diagnostics: diagnostics), diagnostics),
            null,
            null,
            diagnostics);

        var result = await runtime.StartAsync(new ProxyStartRequest(
            fixture.Configuration,
            CorrelationId,
            permit: fixture.CreatePermit(),
            admittedChallenge: Challenge));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, engine.StartCount);
        Assert.AreEqual(3072, fixture.KeySize);
        StringAssert.Contains(writer.ToString(), "stage=RUNTIME_START category=STAGE_COMPLETED");
        AssertNoSensitiveMarkers(writer.ToString(), fixture.LastCompactPermit);
    }

    [TestMethod]
    public async Task OrdinaryInvalidInputsNeverCollapseToAuthorizationUnavailable()
    {
        using var fixture = PermitFixture.Create();
        var invalidClaims = new[]
        {
            new Dictionary<string, object> { ["iss"] = "wrong-issuer" },
            new Dictionary<string, object> { ["aud"] = "wrong-audience" },
            new Dictionary<string, object> { ["product"] = "wrong-product" },
            new Dictionary<string, object> { ["scope"] = "wrong-scope" },
            new Dictionary<string, object> { ["mode"] = "wrong-mode" },
            new Dictionary<string, object> { ["target_pid"] = 9999 },
            new Dictionary<string, object> { ["challenge"] = new string('B', 43) }
        };

        foreach (var overrides in invalidClaims)
        {
            var error = await fixture.CreateVerifier().VerifyAsync(
                fixture.CreatePermit(overrides),
                fixture.Configuration,
                Challenge,
                CancellationToken.None);
            Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
        }

        var configurationError = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermit(new() { ["cfg"] = new string('0', 64) }),
            fixture.Configuration,
            Challenge,
            CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.ConfigurationMismatch, configurationError!.Code);

        var expired = await fixture.CreateVerifier(
                clock: new FixedClock(DateTimeOffset.FromUnixTimeSeconds(Now + 31)))
            .VerifyAsync(
                fixture.CreatePermit(), fixture.Configuration, Challenge, CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.AuthorizationExpired, expired!.Code);
    }

    [TestMethod]
    public void DiagnosticFormatterAndOptInSwitchAcceptOnlyClosedValues()
    {
        using var writer = new StringWriter();
        var diagnostics = new SanitizedTextCoreDiagnosticSink(writer);
        diagnostics.Report(new CoreDiagnosticEvent(
            CoreDiagnosticStage.PermitParse,
            CoreDiagnosticCategory.StageCompleted));
        diagnostics.Report(new CoreDiagnosticEvent(
            (CoreDiagnosticStage)int.MaxValue,
            CoreDiagnosticCategory.StageCompleted));
        diagnostics.Report(new CoreDiagnosticEvent(
            CoreDiagnosticStage.PermitParse,
            (CoreDiagnosticCategory)int.MaxValue));

        Assert.AreEqual(
            "NEKO_CORE_DIAGNOSTIC stage=PERMIT_PARSE category=STAGE_COMPLETED" + Environment.NewLine,
            writer.ToString());
        Assert.IsTrue(HostDiagnosticSink.IsEnabled("1"));
        Assert.IsFalse(HostDiagnosticSink.IsEnabled(null));
        Assert.IsFalse(HostDiagnosticSink.IsEnabled("true"));
        Assert.IsFalse(HostDiagnosticSink.IsEnabled(" 1"));
    }

    private static async Task AssertUnavailableAsync(
        StrictLaunchPermitVerifier verifier,
        SensitivePermit permit,
        ProxyConfiguration configuration)
    {
        var error = await verifier.VerifyAsync(
            permit, configuration, Challenge, CancellationToken.None);
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, error!.Code);
    }

    private static ProxyConfiguration CreateConfiguration() => new(
        ProxyModeKind.Process,
        "pso2.exe",
        "profile-0",
        "server-0",
        targetPid: 4242);

    private static void AssertNoSensitiveMarkers(string output, string? compactPermit)
    {
        var markers = new[]
        {
            compactPermit,
            Challenge,
            "synthetic-subject-marker",
            "synthetic-session-marker",
            "synthetic-installation-marker",
            "synthetic-license-marker",
            "synthetic-jti-marker",
            "permit-secret-marker",
            "jwt-secret-marker",
            "password-marker",
            "access-token-marker",
            "refresh-token-marker",
            "private-key-marker",
            "public-key-marker"
        };
        foreach (var marker in markers)
        {
            if (!string.IsNullOrEmpty(marker))
                Assert.IsFalse(output.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    private sealed class PermitFixture : IDisposable
    {
        private const string KeyId = "neko-prod-key-2";
        private readonly RSA _signer;
        private readonly RsaTrustedPublicKey _trustedKey;
        private readonly ITrustedPublicKeyResolver _resolver;

        private PermitFixture(RSA signer)
        {
            _signer = signer;
            _trustedKey = RsaTrustedPublicKey.FromParameters(signer.ExportParameters(false));
            _resolver = new ImmutableTrustedPublicKeyResolver(
                new Dictionary<string, ITrustedPublicKey> { [KeyId] = _trustedKey });
            Configuration = CreateConfiguration();
        }

        public ProxyConfiguration Configuration { get; }
        public int KeySize => _signer.KeySize;
        public string? LastCompactPermit { get; private set; }

        public static PermitFixture Create() => new(RSA.Create(3072));

        public StrictLaunchPermitVerifier CreateVerifier(
            ITrustedPublicKeyResolver? resolver = null,
            ITrustedUtcClock? clock = null,
            ICanonicalConfigurationSerializer? serializer = null,
            IPermitReplayStore? replayStore = null,
            ICoreDiagnosticSink? diagnostics = null) =>
            new(
                resolver ?? _resolver,
                serializer ?? new S0Rc1CanonicalConfigurationSerializer(),
                clock ?? new FixedClock(),
                replayStore ?? new InMemoryPermitReplayStore(),
                diagnostics);

        public SensitivePermit CreatePermit(Dictionary<string, object>? overrides = null)
        {
            var configurationBytes = new S0Rc1CanonicalConfigurationSerializer()
                .Serialize(Configuration)
                .ToArray();
            var claims = new Dictionary<string, object>
            {
                ["iss"] = "neko-backend",
                ["aud"] = "neko-proxy-core",
                ["sub"] = "synthetic-subject-marker",
                ["sid"] = "synthetic-session-marker",
                ["iid"] = "synthetic-installation-marker",
                ["lid"] = "synthetic-license-marker",
                ["product"] = "neko-family-proxy",
                ["scope"] = "proxy:start",
                ["cfg"] = Convert.ToHexString(SHA256.HashData(configurationBytes)).ToLowerInvariant(),
                ["challenge"] = Challenge,
                ["target_pid"] = 4242,
                ["mode"] = "ProcessMode",
                ["jti"] = "synthetic-jti-marker",
                ["iat"] = Now - 1,
                ["nbf"] = Now - 1,
                ["exp"] = Now + 29
            };
            if (overrides != null)
            {
                foreach (var pair in overrides)
                    claims[pair.Key] = pair.Value;
            }

            var header = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["alg"] = "RS256",
                ["typ"] = "neko-launch+jwt",
                ["kid"] = KeyId
            });
            var signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." +
                               Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));
            var signature = _signer.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            LastCompactPermit = signingInput + "." + Base64Url(signature);
            Assert.IsTrue(SensitivePermit.TryCreate(LastCompactPermit, 4096, out var permit));
            return permit!;
        }

        public void Dispose()
        {
            _trustedKey.Dispose();
            _signer.Dispose();
        }

        private static string Base64Url(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class FixedClock : ITrustedUtcClock
    {
        public FixedClock(DateTimeOffset? utcNow = null, bool isTrusted = true)
        {
            UtcNow = utcNow ?? DateTimeOffset.FromUnixTimeSeconds(Now);
            IsTrusted = isTrusted;
        }

        public DateTimeOffset UtcNow { get; set; }
        public bool IsTrusted { get; }
    }

    private sealed class ThrowingClock : ITrustedUtcClock
    {
        public DateTimeOffset UtcNow => throw new InvalidOperationException("refresh-token-marker");
        public bool IsTrusted => true;
    }

    private sealed class ThrowingKeyResolver : ITrustedPublicKeyResolver
    {
        public bool TryResolve(string keyId, out ITrustedPublicKey? key) =>
            throw new InvalidOperationException("password-marker access-token-marker");
    }

    private sealed class ForeignKeyResolver : ITrustedPublicKeyResolver
    {
        public bool TryResolve(string keyId, out ITrustedPublicKey? key)
        {
            key = new ForeignTrustedPublicKey();
            return true;
        }
    }

    private sealed class ForeignTrustedPublicKey : ITrustedPublicKey
    {
    }

    private sealed class ThrowingSerializer : ICanonicalConfigurationSerializer
    {
        public ReadOnlyMemory<byte> Serialize(ProxyConfiguration configuration) =>
            throw new InvalidOperationException("private-key-marker");
    }

    private sealed class ThrowingReplayStore : IPermitReplayStore
    {
        public bool TryConsume(string permitId, long expiresAtNumericDate) =>
            throw new InvalidOperationException("synthetic-jti-marker");
    }

    private sealed class ThrowingPermitVerifier : IPermitVerifier
    {
        public Task<ProxyError?> VerifyAsync(
            SensitivePermit permit,
            ProxyConfiguration configuration,
            string challenge,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("permit-secret-marker jwt-secret-marker");
    }

    private sealed class ThrowingStartAuthorizer : IProxyStartAuthorizer
    {
        public Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request) =>
            throw new InvalidOperationException("password-marker");
    }

    private sealed class AllowStartAuthorizer : IProxyStartAuthorizer
    {
        public Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request) =>
            Task.FromResult<ProxyError?>(null);
    }

    private sealed class NameOnlyResolver : IProcessResolver
    {
        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class NoOpModeController : IProxyModeController
    {
        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpEngine : IProcessModeEngine
    {
        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
