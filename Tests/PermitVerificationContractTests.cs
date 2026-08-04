using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class PermitVerificationContractTests
{
    [TestMethod]
    public void S0Rc1CanonicalConfigurationMatchesFrozenSyntheticFixture()
    {
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process,
            "pso2.exe",
            "profile-0",
            "server-0",
            targetPid: 4242);
        var serializer = new S0Rc1CanonicalConfigurationSerializer();

        var bytes = serializer.Serialize(configuration).ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.AreEqual(
            "protocolVersion=2\n" +
            "mode=ProcessMode\n" +
            "processName=pso2.exe\n" +
            "targetPid=4242\n" +
            "profileReference=profile-0\n" +
            "serverReference=server-0\n",
            text);
        Assert.AreEqual("92ac70d0f9b100ba664f2bb205b2c042bc1058f779e94e759822d906ea880871", digest);
    }

    [TestMethod]
    public void S0Rc1CanonicalConfigurationRequiresTargetBoundProcessMode()
    {
        var serializer = new S0Rc1CanonicalConfigurationSerializer();

        Assert.ThrowsException<ArgumentException>(() => serializer.Serialize(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "profile-0", "server-0")));
    }

    [TestMethod]
    public async Task ChallengePermitAuthorizerConsumesChallengeAndPassesOpaquePermitToVerifier()
    {
        var verifier = new RecordingPermitVerifier();
        var authorizer = new ChallengePermitStartAuthorizer(verifier);
        Assert.IsTrue(SensitivePermit.TryCreate("header.payload.signature", 4096, out var permit));
        var request = new ProxyStartRequest(
            new ProxyConfiguration(
                ProxyModeKind.Process,
                "pso2.exe",
                "profile-0",
                "server-0",
                targetPid: 4242),
            "0123456789abcdef0123456789abcdef",
            permit: permit,
            admittedChallenge: "admitted-challenge");

        var error = await authorizer.AuthorizeAsync(request);

        Assert.IsNull(error);
        Assert.AreEqual("admitted-challenge", verifier.Challenge);
        Assert.AreSame(permit, verifier.Permit);
    }

    [TestMethod]
    public async Task ChallengePermitAuthorizerFailsClosedWithoutAdmittedChallenge()
    {
        var verifier = new RecordingPermitVerifier();
        var authorizer = new ChallengePermitStartAuthorizer(verifier);
        Assert.IsTrue(SensitivePermit.TryCreate("header.payload.signature", 4096, out var permit));
        var request = new ProxyStartRequest(
            new ProxyConfiguration(
                ProxyModeKind.Process,
                "pso2.exe",
                "profile-0",
                "server-0",
                targetPid: 4242),
            "0123456789abcdef0123456789abcdef",
            permit: permit);

        var replay = await authorizer.AuthorizeAsync(request);

        Assert.AreEqual(ProxyErrorCode.AuthorizationRequired, replay!.Code);
        Assert.AreEqual(0, verifier.CallCount);
    }

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

    private sealed class RecordingPermitVerifier : IPermitVerifier
    {
        public int CallCount { get; private set; }

        public SensitivePermit? Permit { get; private set; }

        public string? Challenge { get; private set; }

        public Task<ProxyError?> VerifyAsync(
            SensitivePermit permit,
            ProxyConfiguration configuration,
            string challenge,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Permit = permit;
            Challenge = challenge;
            return Task.FromResult<ProxyError?>(null);
        }
    }
}
