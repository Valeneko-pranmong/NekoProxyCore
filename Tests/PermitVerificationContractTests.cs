using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class PermitVerificationContractTests
{
    [TestMethod]
    public void SensitivePermitNeverRendersItsValue()
    {
        const string sentinel = "permit-secret-sentinel";

        Assert.IsTrue(SensitivePermit.TryCreate(sentinel, 128, out var permit));
        Assert.AreEqual("[REDACTED]", permit!.ToString());
        Assert.IsFalse(permit.ToString().Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void SensitivePermitRejectsEmptyAndValuesBeyondTheSuppliedContractBound()
    {
        Assert.IsFalse(SensitivePermit.TryCreate(string.Empty, 8, out _));
        Assert.IsTrue(SensitivePermit.TryCreate(new string('x', 8), 8, out _));
        Assert.IsFalse(SensitivePermit.TryCreate(new string('x', 9), 8, out _));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            SensitivePermit.TryCreate("value", 0, out _));
    }

    [TestMethod]
    public void FixtureGateAcceptsExactRevisionAndHash()
    {
        var expected = new ContractFixtureIdentity("synthetic-revision", new byte[32]);
        var actual = new ContractFixtureIdentity("synthetic-revision", new byte[32]);

        ContractFixtureGate.EnsureMatch(expected, actual);
    }

    [TestMethod]
    public void FixtureIdentityDoesNotExposeRawRevisionAsPublicProperty()
    {
        var publicProperties = typeof(ContractFixtureIdentity).GetProperties();

        Assert.IsFalse(publicProperties.Any(property =>
            string.Equals(property.Name, "Revision", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void FixtureGateRejectsRevisionMismatchWithoutRenderingEitherRevision()
    {
        const string expectedSentinel = "expected-revision-sentinel";
        const string actualSentinel = "actual-revision-sentinel";
        var expected = new ContractFixtureIdentity(expectedSentinel, new byte[32]);
        var actual = new ContractFixtureIdentity(actualSentinel, new byte[32]);

        var exception = Assert.ThrowsException<ContractFixtureMismatchException>(
            () => ContractFixtureGate.EnsureMatch(expected, actual));

        Assert.AreEqual("Security contract fixture identity does not match.", exception.Message);
        Assert.IsFalse(exception.ToString().Contains(expectedSentinel, StringComparison.Ordinal));
        Assert.IsFalse(exception.ToString().Contains(actualSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void FixtureGateRejectsHashMismatch()
    {
        var expectedHash = new byte[32];
        var actualHash = new byte[32];
        actualHash[^1] = 1;

        Assert.ThrowsException<ContractFixtureMismatchException>(() =>
            ContractFixtureGate.EnsureMatch(
                new ContractFixtureIdentity("synthetic-revision", expectedHash),
                new ContractFixtureIdentity("synthetic-revision", actualHash)));
    }

    [TestMethod]
    public void FixtureIdentityRequiresSha256LengthWithoutRenderingRevision()
    {
        const string revisionSentinel = "invalid-hash-revision-sentinel";

        var exception = Assert.ThrowsException<ArgumentException>(() =>
            new ContractFixtureIdentity(revisionSentinel, new byte[31]));

        Assert.IsFalse(exception.ToString().Contains(revisionSentinel, StringComparison.Ordinal));
    }
}
