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
public sealed class LiteVerifierSecurityCoverageTests
{
    private const long Now = 2_000_000_001;
    private const string Kid = "neko-prod-key-2";
    private const string Challenge = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [DataTestMethod]
    [DataRow("iss")]
    [DataRow("aud")]
    [DataRow("sub")]
    [DataRow("product")]
    [DataRow("scope")]
    [DataRow("challenge")]
    [DataRow("jti")]
    [DataRow("iat")]
    [DataRow("exp")]
    public async Task EveryRequiredLiteClaimIsRequired(string claimName)
    {
        using var fixture = PermitFixture.Create();
        var error = await fixture.CreateVerifier().VerifyAsync(
            fixture.CreatePermitWithoutClaims(claimName), Challenge, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task NoneAlgorithmAndWrongTypeAreRejected()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();

        var noneAlgorithm = await verifier.VerifyAsync(
            fixture.CreatePermit(algorithm: "none"), Challenge, CancellationToken.None);
        var wrongType = await verifier.VerifyAsync(
            fixture.CreatePermit(type: "JWT"), Challenge, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, noneAlgorithm!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, wrongType!.Code);
    }

    [TestMethod]
    public async Task ExcessivePermitLifetimeIsRejected()
    {
        using var fixture = PermitFixture.Create();
        var permit = fixture.CreatePermit(new() { ["exp"] = Now + 31 });

        var error = await fixture.CreateVerifier().VerifyAsync(permit, Challenge, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
    }

    [TestMethod]
    public async Task MalformedJwtStructureAndEncodingAreRejected()
    {
        using var fixture = PermitFixture.Create();
        var verifier = fixture.CreateVerifier();
        var malformedPermits = new[]
        {
            "a.b",
            "a.b.c.d",
            "a!.b.c",
            Base64Url(new byte[] { 0xff }) + ".b.c",
            new string('a', 4097)
        };

        foreach (var compact in malformedPermits)
        {
            Assert.IsTrue(SensitivePermit.TryCreate(compact, 5000, out var permit));
            var error = await verifier.VerifyAsync(permit!, Challenge, CancellationToken.None);
            Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, error!.Code);
        }
    }

    [TestMethod]
    public async Task TamperedHeaderAndPayloadAreRejected()
    {
        using var fixture = PermitFixture.Create();
        var original = fixture.CreateCompactPermit().Split('.');
        var tamperedHeader = Base64Url(Encoding.UTF8.GetBytes(
                                 "{\"alg\":\"RS256\",\"typ\":\"neko-launch+jwt\",\"kid\":\"neko-prod-key-2\",\"x\":true}")) +
                             "." + original[1] + "." + original[2];
        var tamperedPayload = original[0] + "." +
                              Base64Url(Encoding.UTF8.GetBytes("{\"iss\":\"neko-backend\"}")) +
                              "." + original[2];

        Assert.IsTrue(SensitivePermit.TryCreate(tamperedHeader, 4096, out var headerPermit));
        Assert.IsTrue(SensitivePermit.TryCreate(tamperedPayload, 4096, out var payloadPermit));

        var headerError = await fixture.CreateVerifier().VerifyAsync(headerPermit!, Challenge, CancellationToken.None);
        var payloadError = await fixture.CreateVerifier().VerifyAsync(payloadPermit!, Challenge, CancellationToken.None);

        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, headerError!.Code);
        Assert.AreEqual(ProxyErrorCode.AuthorizationInvalid, payloadError!.Code);
    }

    private sealed class PermitFixture : IDisposable
    {
        private readonly RSA _signer;
        private readonly ITrustedPublicKeyResolver _resolver;

        private PermitFixture(RSA signer)
        {
            _signer = signer;
            _resolver = new ImmutableTrustedPublicKeyResolver(
                new Dictionary<string, ITrustedPublicKey>
                {
                    [Kid] = RsaTrustedPublicKey.FromParameters(signer.ExportParameters(false))
                });
        }

        public static PermitFixture Create() => new(RSA.Create(3072));

        public StrictLaunchPermitVerifier CreateVerifier() => new(
            _resolver,
            new FixedClock(),
            new InMemoryPermitReplayStore());

        public SensitivePermit CreatePermit(
            Dictionary<string, object>? claimOverrides = null,
            string algorithm = "RS256",
            string type = "neko-launch+jwt")
        {
            var claims = CreateClaims();
            if (claimOverrides != null)
            {
                foreach (var claim in claimOverrides)
                    claims[claim.Key] = claim.Value;
            }

            return Sign(JsonSerializer.Serialize(claims), algorithm, type);
        }

        public string CreateCompactPermit() => CreateCompact(JsonSerializer.Serialize(CreateClaims()));

        public SensitivePermit CreatePermitWithoutClaims(string claimName)
        {
            var claims = CreateClaims();
            claims.Remove(claimName);
            return Sign(JsonSerializer.Serialize(claims));
        }

        public void Dispose() => _signer.Dispose();

        private SensitivePermit Sign(string payload, string algorithm = "RS256", string type = "neko-launch+jwt")
        {
            var compact = CreateCompact(payload, algorithm, type);
            Assert.IsTrue(SensitivePermit.TryCreate(compact, 4096, out var permit));
            return permit!;
        }

        private string CreateCompact(string payload, string algorithm = "RS256", string type = "neko-launch+jwt")
        {
            var header = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["alg"] = algorithm,
                ["typ"] = type,
                ["kid"] = Kid
            });
            var signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." +
                               Base64Url(Encoding.UTF8.GetBytes(payload));
            var signature = _signer.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return signingInput + "." + Base64Url(signature);
        }

        private static Dictionary<string, object> CreateClaims() => new()
        {
            ["iss"] = "neko-backend",
            ["aud"] = "neko-proxy-core",
            ["sub"] = "synthetic-subject",
            ["product"] = "neko-family-proxy",
            ["scope"] = "proxy:start",
            ["challenge"] = Challenge,
            ["jti"] = "security-coverage-jti",
            ["iat"] = Now - 1,
            ["nbf"] = Now - 1,
            ["exp"] = Now + 29
        };
    }

    private sealed class FixedClock : ITrustedUtcClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.FromUnixTimeSeconds(Now);
        public bool IsTrusted => true;
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
