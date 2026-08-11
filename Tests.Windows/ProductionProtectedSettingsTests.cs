using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Host;
using NekoProxyCore.Legacy;

namespace Tests.Windows;

[TestClass]
public sealed class ProductionProtectedSettingsTests
{
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(31)]
    [DataRow(33)]
    public void ProductionKeyLoaderRejectsMissingOrWrongLengthMaterial(int length)
    {
        using var stream = new MemoryStream(new byte[length]);

        var exception = Assert.ThrowsException<ProtectedSettingsException>(() =>
            ProductionProtectedSettings.LoadKey(stream));

        Assert.AreEqual("Protected runtime settings are unavailable or invalid.", exception.Message);
    }

    [TestMethod]
    public void ProductionKeyLoaderAcceptsExactlyThirtyTwoBytes()
    {
        var input = RandomNumberGenerator.GetBytes(ProtectedSettingsPayload.KeySizeBytes);
        using var stream = new MemoryStream(input, writable: false);

        var loaded = ProductionProtectedSettings.LoadKey(stream);

        CollectionAssert.AreEqual(input, loaded);
        CryptographicOperations.ZeroMemory(input);
        CryptographicOperations.ZeroMemory(loaded);
    }
}
