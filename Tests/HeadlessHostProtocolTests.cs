using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host.Protocol;

namespace Tests;

[TestClass]
public sealed class HeadlessHostProtocolTests
{
    [TestMethod]
    public void ValidStartRequestProducesValidatedConfiguration()
    {
        const string json = "{\"version\":1,\"command\":\"start\",\"correlationId\":\"launcher-001\",\"processName\":\"pso2.exe\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}";

        var parsed = ControlProtocol.TryParseRequest(json, out var request, out var error);

        Assert.IsTrue(parsed);
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.Start, request!.Command);
        Assert.IsTrue(request.TryCreateStartRequest(out var startRequest, out error));
        Assert.AreEqual("pso2.exe", startRequest!.Configuration.ProcessName);
        Assert.AreEqual("profile-0", startRequest.Configuration.ProfileReference);
        Assert.AreEqual("server-0", startRequest.Configuration.ServerReference);
    }

    [DataTestMethod]
    [DataRow("{\"version\":1,\"command\":\"status\",\"correlationId\":\"launcher-002\"}", ControlCommand.Status)]
    [DataRow("{\"version\":1,\"command\":\"stop\",\"correlationId\":\"launcher-003\"}", ControlCommand.Stop)]
    public void StatusAndStopRequestsAreAccepted(string json, ControlCommand expected)
    {
        Assert.IsTrue(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(expected, request!.Command);
    }

    [DataTestMethod]
    [DataRow("{\"version\":2,\"command\":\"status\",\"correlationId\":\"launcher-002\"}")]
    [DataRow("{\"version\":1,\"command\":\"launch\",\"correlationId\":\"launcher-002\"}")]
    [DataRow("{\"version\":1,\"command\":\"0\",\"correlationId\":\"launcher-002\"}")]
    [DataRow("{\"version\":1,\"command\":\"1\",\"correlationId\":\"launcher-002\"}")]
    [DataRow("{\"version\":1,\"command\":\"2\",\"correlationId\":\"launcher-002\"}")]
    [DataRow("not-json")]
    [DataRow("{\"version\":1,\"command\":\"status\",\"correlationId\":\"password=sentinel\"}")]
    public void InvalidRequestsReturnOnlyTypedConfigurationError(string json)
    {
        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(request);
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, error!.ErrorCode);
        Assert.IsFalse(ControlProtocol.Serialize(error).Contains("sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OversizedFrameIsRejected()
    {
        var json = new string('x', ControlProtocol.MaxFrameBytes + 1);

        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out _, out var error));
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, error!.ErrorCode);
    }

    [TestMethod]
    public void StartRejectsNonCanonicalTargetAndSecretLikeReferences()
    {
        const string json = "{\"version\":1,\"command\":\"start\",\"correlationId\":\"launcher-004\",\"processName\":\"other.exe\",\"profileReference\":\"profile-0\",\"serverReference\":\"password=sentinel\"}";

        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out _, out var error));
        var serialized = ControlProtocol.Serialize(error!);
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, error!.ErrorCode);
        Assert.IsFalse(serialized.Contains("sentinel", StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("message", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RuntimeResultsSerializeOnlyAllowListedFields()
    {
        var result = ProxyResult.Failure(
            ProxyStatusKind.Failed,
            "launcher-005",
            new ProxyError(ProxyErrorCode.StartFailed, "password=sentinel-token"));

        var json = ControlProtocol.Serialize(ControlResponse.FromResult(result));

        StringAssert.Contains(json, "\"version\":1");
        StringAssert.Contains(json, "\"kind\":\"result\"");
        StringAssert.Contains(json, "\"correlationId\":\"launcher-005\"");
        StringAssert.Contains(json, "\"status\":\"Failed\"");
        StringAssert.Contains(json, "\"succeeded\":false");
        StringAssert.Contains(json, "\"errorCode\":\"StartFailed\"");
        Assert.IsFalse(json.Contains("sentinel-token", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("timestamp", StringComparison.OrdinalIgnoreCase));
    }
}
