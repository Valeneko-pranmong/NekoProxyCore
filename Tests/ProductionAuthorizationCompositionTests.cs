using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class ProductionAuthorizationCompositionTests
{
    [TestMethod]
    public async Task CurrentReleaseCompositionFailsClosedWithoutApprovedReleaseMaterial()
    {
        var authorizer = ProductionAuthorizationComposition.CreateStartAuthorizer();
        var request = new ProxyStartRequest(new ProxyConfiguration(
            ProxyModeKind.Process,
            "pso2.exe",
            "profile-0",
            "server-0",
            targetPid: 4242));

        var error = await authorizer.AuthorizeAsync(request);

        Assert.IsInstanceOfType(authorizer, typeof(AuthorizationRequiredStartAuthorizer));
        Assert.AreEqual(ProxyErrorCode.AuthorizationRequired, error!.Code);
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
}
