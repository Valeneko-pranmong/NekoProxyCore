using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host.Protocol;

namespace Tests;

[TestClass]
public sealed class HeadlessHostProtocolTests
{
    [TestMethod]
    public void LauncherCanonicalPipeNameIsUsed()
    {
        Assert.AreEqual("NekoProxyCoreControl", ControlProtocol.PipeName);
    }

    [TestMethod]
    public void LauncherCanonicalChallengeRequestIsAccepted()
    {
        const string json = "{\"type\":\"challenge\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}";

        Assert.IsTrue(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.Challenge, request!.Command);
    }

    [TestMethod]
    public void LauncherCanonicalStartRequestProducesTargetBoundConfiguration()
    {
        const string json = "{\"type\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"protocolVersion\":2,\"mode\":\"ProcessMode\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}";
        var challenges = new CoreChallengeService();
        challenges.Issue();

        var parsed = ControlProtocol.TryParseRequest(json, challenges, out var request, out var error);

        Assert.IsTrue(parsed);
        Assert.IsNull(error);
        Assert.IsTrue(request!.TryCreateStartRequest(out var startRequest, out error));
        Assert.AreEqual((uint)4242, startRequest!.Configuration.TargetPid);
    }

    [TestMethod]
    public void ValidStartRequestProducesTargetBoundConfiguration()
    {
        const string json = "{\"type\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"protocolVersion\":2,\"mode\":\"ProcessMode\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}";

        var challenges = new CoreChallengeService();
        challenges.Issue();
        var parsed = ControlProtocol.TryParseRequest(json, challenges, out var request, out var error);

        Assert.IsTrue(parsed);
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.Start, request!.Command);
        Assert.IsTrue(request.TryCreateStartRequest(out var startRequest, out error));
        Assert.AreEqual("pso2.exe", startRequest!.Configuration.ProcessName);
        Assert.AreEqual((uint)4242, startRequest.Configuration.TargetPid);
        Assert.AreEqual("profile-0", startRequest.Configuration.ProfileReference);
        Assert.AreEqual("server-0", startRequest.Configuration.ServerReference);
        Assert.AreEqual("[REDACTED]", request.Permit!.ToString());
        Assert.AreSame(request.Permit, startRequest.Permit);
    }

    [DataTestMethod]
    [DataRow("{\"type\":\"status\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}", ControlCommand.Status)]
        [DataRow("{\"type\":\"stop\",\"correlationId\":\"fedcba9876543210fedcba9876543210\"}", ControlCommand.Stop)]
    public void StatusAndStopRequestsAreAccepted(string json, ControlCommand expected)
    {
        Assert.IsTrue(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(expected, request!.Command);
    }

    [TestMethod]
    public void ChallengeRequestIsAcceptedWithoutStartFields()
    {
        const string json = "{\"type\":\"challenge\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}";

        Assert.IsTrue(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.Challenge, request!.Command);
    }

    [DataTestMethod]
    [DataRow("{\"version\":1,\"command\":\"status\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}")]
    [DataRow("{\"version\":2,\"command\":\"Status\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}")]
    [DataRow("{\"version\":2,\"command\":\"status\",\"correlationId\":\"launcher-002\"}")]
    [DataRow("not-json")]
    [DataRow("{\"version\":2,\"command\":\"status\",\"correlationId\":\"password=sentinel\"}")]
    [DataRow("{\"version\":2,\"command\":\"status\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"extra\":true}")]
    [DataRow("{\"version\":2,\"version\":2,\"command\":\"status\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}")]
    [DataRow("{\"type\":\"unknown\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}")]
    [DataRow("[]")]
    public void InvalidRequestsReturnOnlyTypedProtocolError(string json)
    {
        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(request);
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, error!.ErrorCode);
        Assert.IsFalse(ControlProtocol.Serialize(error).Contains("sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OversizedFrameIsRejected()
    {
        var json = new string('x', ControlProtocol.MaxFrameBytes + 1);

        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out _, out var error));
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, error!.ErrorCode);
    }

    [DataTestMethod]
    [DataRow("{\"version\":2,\"command\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"processName\":\"pso2.exe\",\"targetPid\":0,\"mode\":\"ProcessMode\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}")]
    [DataRow("{\"version\":2,\"command\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"processName\":\"PSO2.EXE\",\"targetPid\":4242,\"mode\":\"ProcessMode\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}")]
    [DataRow("{\"version\":2,\"command\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"mode\":\"ProcessMode\",\"profileReference\":\"profile-0000000\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}")]
    [DataRow("{\"version\":2,\"command\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"mode\":\"ProcessMode\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"not-compact\"}")]
    [DataRow("{\"version\":2,\"command\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"mode\":\"ProcessMode\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.pay load.signature\"}")]
    [DataRow("{\"version\":2,\"command\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"mode\":\"ProcessMode\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signaturé\"}")]
    public void StartRequiresExactS0Rc1Fields(string json)
    {
        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out _, out var error));
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, error!.ErrorCode);
    }

    [TestMethod]
    public void ChallengeResponseSerializesLauncherCanonicalFields()
    {
        var json = ControlProtocol.SerializeChallenge(
            "0123456789abcdef0123456789abcdef",
            new CoreChallenge("0123456789012345678901234567890123456789012"));

        Assert.AreEqual(
            "{\"type\":\"challengeResponse\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"challenge\":\"0123456789012345678901234567890123456789012\"}",
            json);
    }

    [TestMethod]
    public void RuntimeResultsSerializeOnlyAllowListedFields()
    {
        var result = ProxyResult.Failure(
            ProxyStatusKind.Failed,
            "0123456789abcdef0123456789abcdef",
            new ProxyError(ProxyErrorCode.StartFailed, "password=sentinel-token"));

        var json = ControlProtocol.Serialize(ControlResponse.FromResult(result));

        Assert.IsTrue(json.Contains("\"type\":\"result\"", StringComparison.Ordinal));
        StringAssert.Contains(json, "\"correlationId\":\"0123456789abcdef0123456789abcdef\"");
        StringAssert.Contains(json, "\"status\":\"Failed\"");
        StringAssert.Contains(json, "\"succeeded\":false");
        StringAssert.Contains(json, "\"errorCode\":\"StartFailed\"");
        Assert.IsFalse(json.Contains("sentinel-token", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("timestamp", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void StopResponseEchoesLauncherRequestCorrelationId()
    {
        var runtimeResult = ProxyResult.Success(ProxyStatusKind.Stopped, "runtime-correlation");

        var json = ControlProtocol.Serialize(
            ControlResponse.FromResult(runtimeResult, "0123456789abcdef0123456789abcdef"),
            "stopResponse");

        Assert.IsTrue(json.Contains(
            "\"correlationId\":\"0123456789abcdef0123456789abcdef\"",
            StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("runtime-correlation", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ShutdownRequestIsAcceptedWithCanonicalShape()
    {
        const string json = "{\"type\":\"shutdown\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}";

        Assert.IsTrue(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.Shutdown, request!.Command);
    }

    [DataTestMethod]
    [DataRow("{\"type\":\"shutdown\"}")]
    [DataRow("{\"type\":\"shutdown\",\"correlationId\":\"invalid\"}")]
    [DataRow("{\"type\":\"shutdown\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"extra\":true}")]
    [DataRow("{\"type\":\"shutdown\",\"correlationId\":12345678901234567890123456789012}")]
    [DataRow("{\"type\":\"shutdown\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"permit\":\"header.payload.signature\"}")]
    public void MalformedShutdownRequestsAreRejected(string json)
    {
        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(request);
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, error!.ErrorCode);
    }

    [TestMethod]
    public void ShutdownResponseSerializesCanonicalFields()
    {
        var json = ControlProtocol.Serialize(ControlResponse.ShutdownSuccess("0123456789abcdef0123456789abcdef"));

        Assert.AreEqual(
            "{\"type\":\"shutdownResponse\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"status\":\"Stopped\",\"succeeded\":true}",
            json);
    }

    [TestMethod]
    public void ShutdownDoesNotConsumeOrBypassStartChallengeAdmission()
    {
        const string shutdown = "{\"type\":\"shutdown\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}";
        const string start = "{\"type\":\"start\",\"correlationId\":\"fedcba9876543210fedcba9876543210\",\"protocolVersion\":2,\"mode\":\"ProcessMode\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}";
        var challenges = new CoreChallengeService();

        Assert.IsTrue(ControlProtocol.TryParseRequest(shutdown, challenges, out _, out _));
        Assert.IsFalse(ControlProtocol.TryParseRequest(start, challenges, out _, out var missingChallengeError));
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, missingChallengeError!.ErrorCode);

        challenges.Issue();
        Assert.IsTrue(ControlProtocol.TryParseRequest(shutdown, challenges, out _, out _));
        Assert.IsTrue(ControlProtocol.TryParseRequest(start, challenges, out var admittedStart, out _));
        Assert.AreEqual(ControlCommand.Start, admittedStart!.Command);
    }

    [TestMethod]
    public void NonWireRuntimeErrorMapsToFrozenUnavailableCode()
    {
        var result = ProxyResult.Failure(
            ProxyStatusKind.Failed,
            "0123456789abcdef0123456789abcdef",
            new ProxyError(ProxyErrorCode.InvalidConfiguration, "ignored"));

        var json = ControlProtocol.Serialize(ControlResponse.FromResult(result));

        Assert.IsTrue(json.Contains("\"errorCode\":\"AuthorizationUnavailable\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("InvalidConfiguration", StringComparison.Ordinal));
    }
}
