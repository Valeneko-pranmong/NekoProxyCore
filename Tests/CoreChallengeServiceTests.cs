using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class CoreChallengeServiceTests
{
    [TestMethod]
    public void IssueCreatesChallengeWithAtLeast256BitsOfEntropy()
    {
        var service = new CoreChallengeService();

        var challenge = service.Issue();
        var encoded = challenge.Value
            .Replace('-', '+')
            .Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        var decoded = Convert.FromBase64String(encoded);

        Assert.IsTrue(decoded.Length >= 32);
    }
}
