using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host.Authorization;

namespace Tests;

[TestClass]
public sealed class ProductionAuthorizationCompositionTests
{
    [TestMethod]
    public void ProductionPublicKeyLoaderReturnsTheExactBundledAllowList()
    {
        using var signer = RSA.Create(2048);
        var expected = signer.ExportParameters(false);
        var json = "{\"version\":1,\"keys\":[{\"kid\":\"neko-prod-key-2\",\"modulus\":\"" +
                   Base64Url(expected.Modulus!) + "\",\"exponent\":\"" +
                   Base64Url(expected.Exponent!) + "\"}]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var keys = ProductionPublicKeys.Load(stream);

        Assert.AreEqual(1, keys.Count);
        CollectionAssert.AreEqual(expected.Modulus, keys[ProductionPublicKeys.CanonicalKeyId].Modulus);
        CollectionAssert.AreEqual(expected.Exponent, keys[ProductionPublicKeys.CanonicalKeyId].Exponent);
    }

    [TestMethod]
    public void ProductionPublicKeyLoaderRejectsMalformedOrEmptyAllowLists()
    {
        var invalidManifests = new[]
        {
            "{}",
            "{\"version\":1,\"keys\":[]}",
            "{\"version\":1,\"version\":1,\"keys\":[]}",
            "{\"version\":1,\"keys\":[{\"kid\":\"key-1\",\"modulus\":\"not-base64url!\",\"exponent\":\"AQAB\"}]}",
            "{\"version\":1,\"keys\":[{\"kid\":\"another-key\",\"modulus\":\"" +
                Base64Url(new byte[256]) + "\",\"exponent\":\"AQAB\"}]}",
            "{\"version\":1,\"keys\":[{\"kid\":\"neko-prod-key-1\",\"modulus\":\"" +
                Base64Url(new byte[256]) + "\",\"exponent\":\"AQAB\"}]}"
        };

        foreach (var json in invalidManifests)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            Assert.ThrowsException<InvalidOperationException>(() => ProductionPublicKeys.Load(stream));
        }
    }

    [TestMethod]
    public void BundledProductionPublicKeyUsesLauncherCanonicalKeyId()
    {
        var keys = ProductionPublicKeys.LoadBundled();

        Assert.AreEqual("neko-prod-key-2", ProductionPublicKeys.CanonicalKeyId);
        Assert.AreEqual(1, keys.Count);
        Assert.IsTrue(keys.ContainsKey(ProductionPublicKeys.CanonicalKeyId));
        Assert.IsFalse(keys.ContainsKey("neko-prod-key-1"));

        using var rsa = RSA.Create();
        rsa.ImportParameters(keys[ProductionPublicKeys.CanonicalKeyId]);
        Assert.AreEqual(3072, rsa.KeySize);
        Assert.AreEqual(
            "4a0ef40a483c6a4f294724ea62d0ae55357176e196c9747defec06769a0d0801",
            Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant());
    }

    [TestMethod]
    public async Task CompositionBuildsStrictPermitAuthorizerFromApprovedPublicKeys()
    {
        using var signer = RSA.Create(2048);
        var authorizer = ProductionAuthorizationComposition.CreateStartAuthorizer(
            new Dictionary<string, RSAParameters>
            {
                ["production-s0-rs256-01"] = signer.ExportParameters(false)
            });
        var request = new ProxyStartRequest(new ProxyConfiguration(
            ProxyModeKind.Process,
            "pso2.exe",
            "profile-0",
            "server-0",
            targetPid: 4242));

        var error = await authorizer.AuthorizeAsync(request);

        Assert.IsInstanceOfType(authorizer, typeof(ChallengePermitStartAuthorizer));
        Assert.AreEqual(ProxyErrorCode.AuthorizationRequired, error!.Code);
    }

    [TestMethod]
    public void CompositionRejectsAnEmptyPublicKeyAllowList()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            ProductionAuthorizationComposition.CreateStartAuthorizer(
                new Dictionary<string, RSAParameters>()));
    }

    [TestMethod]
    public void HostLoadsProductionTrustBeforeConstructingTheEngine()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "NekoProxyCore.Host",
            "Program.cs"));

        var keyLoadIndex = source.IndexOf("ProductionPublicKeys.LoadBundled()", StringComparison.Ordinal);
        var engineIndex = source.IndexOf("new NetchProcessModeEngine", StringComparison.Ordinal);

        Assert.IsTrue(keyLoadIndex >= 0, "The production public-key load is missing.");
        Assert.IsTrue(engineIndex > keyLoadIndex, "Trust material must fail closed before engine construction.");
    }

    [TestMethod]
    public void CompositionPinsTheAcceptedLaunchContractIdentity()
    {
        Assert.AreEqual("NEKO-AUTH-S0", ProductionAuthorizationComposition.ContractId);
        Assert.AreEqual("s0-rc1", ProductionAuthorizationComposition.ContractRevision);
        Assert.AreEqual(
            "6697351b6b280afc566fedaaa1a6cfe207b1ea1d803c2eb613b4c1a891e192df",
            ProductionAuthorizationComposition.ContractPackageSha256);
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
