using System;
using System.Linq;
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
    public void ProtocolV2StartFailsClosedWithoutConsumingChallenge()
    {
        const string json = "{\"type\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"protocolVersion\":2,\"mode\":\"ProcessMode\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}";
        var challenges = new CoreChallengeService();
        challenges.Issue();

        Assert.IsFalse(ControlProtocol.TryParseRequest(json, challenges, out _, out _));
        Assert.IsTrue(ControlProtocol.TryParseRequest(ValidV3Start(), challenges, out var request, out var error));
        Assert.IsNull(error);
        Assert.IsTrue(request!.TryCreateStartRequest(out var startRequest, out error));
        Assert.AreEqual((uint)4242, startRequest!.Configuration.TargetPid);
        Assert.IsNotNull(startRequest.RuntimeConfig);
    }

    [TestMethod]
    public void StartCorrelationWithTrailingNewlineRemainsCompatible()
    {
        var json = ValidV3Start().Replace(
            "0123456789abcdef0123456789abcdef",
            "0123456789abcdef0123456789abcdef\\n",
            StringComparison.Ordinal);
        var challenges = new CoreChallengeService();
        challenges.Issue();

        Assert.IsTrue(ControlProtocol.TryParseRequest(json, challenges, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.Start, request!.Command);
    }

    [TestMethod]
    public void ValidStartRequestProducesTargetBoundConfiguration()
    {
        var json = ValidV3Start();

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

    [TestMethod]
    public void RuntimeConfigurationCatalogRequestUsesExactReadOnlyShape()
    {
        const string json = "{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}";

        Assert.IsTrue(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.RuntimeConfigCatalog, request!.Command);
        Assert.IsNull(request.ProfileReference);
        Assert.IsNull(request.ServerReference);
    }

    [TestMethod]
    public void RuntimeConfigurationValidationRequestCarriesOnlyOpaqueReferences()
    {
        const string json = "{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-12\",\"serverReference\":\"server-3\"}";

        Assert.IsTrue(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(ControlCommand.RuntimeConfigValidate, request!.Command);
        Assert.AreEqual("profile-12", request.ProfileReference);
        Assert.AreEqual("server-3", request.ServerReference);
        Assert.IsNull(request.Permit);
    }

    [DataTestMethod]
    [DataRow("{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"extra\":true}")]
    [DataRow("{\"type\":\"runtimeConfigCatalog\",\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}")]
    [DataRow("{\"type\":\"runtimeConfigCatalog\"}")]
    [DataRow("{\"type\":123,\"correlationId\":\"0123456789abcdef0123456789abcdef\"}")]
    [DataRow("{\"type\":\"runtimeConfigCatalog\",\"correlationId\":123}")]
    [DataRow("{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"INVALID\"}")]
    [DataRow("{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"0123456789abcdef0123456789abcdef\\n\"}")]
    [DataRow("{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"}")]
    [DataRow("{\"type\":123,\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"INVALID\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":0,\"serverReference\":\"server-0\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-x\",\"serverReference\":\"server-0\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0000000\",\"serverReference\":\"server-0\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-x\"}")]
    [DataRow("{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0000000\"}")]
    public void RuntimeConfigurationRequestsRejectNonCanonicalShapes(string json)
    {
        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(request);
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, error!.ErrorCode);
    }

    [TestMethod]
    public void RuntimeConfigurationRequestsDoNotIssueOrConsumeChallengeState()
    {
        const string catalog = "{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"11111111111111111111111111111111\"}";
        const string validation = "{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"22222222222222222222222222222222\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}";
        var start = ValidV3Start("33333333333333333333333333333333");
        var challenges = new CoreChallengeService();

        Assert.IsTrue(ControlProtocol.TryParseRequest(catalog, challenges, out _, out _));
        Assert.IsTrue(ControlProtocol.TryParseRequest(validation, challenges, out _, out _));
        Assert.IsFalse(ControlProtocol.TryParseRequest(start, challenges, out _, out _));

        challenges.Issue();
        Assert.IsTrue(ControlProtocol.TryParseRequest(catalog, challenges, out _, out _));
        Assert.IsTrue(ControlProtocol.TryParseRequest(validation, challenges, out _, out _));
        Assert.IsTrue(ControlProtocol.TryParseRequest(start, challenges, out var admittedStart, out _));
        Assert.AreEqual(ControlCommand.Start, admittedStart!.Command);
    }

    [TestMethod]
    public void RuntimeConfigurationCatalogResponseSerializesOnlyAllowListedFields()
    {
        var result = ProcessModeConfigurationCatalogResult.Success(new[]
        {
            new ProcessModeConfigurationCandidate("profile-12", "server-3", true, 1)
        });

        var json = ControlProtocol.SerializeRuntimeConfigCatalog(
            "0123456789abcdef0123456789abcdef",
            result);

        Assert.AreEqual(
            "{\"type\":\"runtimeConfigCatalogResponse\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"succeeded\":true,\"candidates\":[{\"profileReference\":\"profile-12\",\"serverReference\":\"server-3\",\"relationshipValid\":true,\"processModeMatchCount\":1}]}",
            json);
    }

    [TestMethod]
    public void EmptyRuntimeConfigurationCatalogIsSuccessfulWithoutFallbackCandidate()
    {
        var json = ControlProtocol.SerializeRuntimeConfigCatalog(
            "0123456789abcdef0123456789abcdef",
            ProcessModeConfigurationCatalogResult.Success(
                Array.Empty<ProcessModeConfigurationCandidate>()));

        Assert.AreEqual(
            "{\"type\":\"runtimeConfigCatalogResponse\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"succeeded\":true,\"candidates\":[]}",
            json);
    }

    [TestMethod]
    public void RuntimeConfigurationCatalogFailureUsesFixedReasonWithoutCandidates()
    {
        var json = ControlProtocol.SerializeRuntimeConfigCatalog(
            "0123456789abcdef0123456789abcdef",
            ProcessModeConfigurationCatalogResult.Failure(
                ProcessModeConfigurationCatalogFailureReason.CatalogTooLarge));

        Assert.AreEqual(
            "{\"type\":\"runtimeConfigCatalogResponse\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"succeeded\":false,\"reason\":\"CatalogTooLarge\"}",
            json);
    }

    [TestMethod]
    public void MaximumCatalogResponseFitsWithinTheBoundedControlFrame()
    {
        var result = ProcessModeConfigurationCatalogResult.Success(
            Enumerable.Range(0, ProcessModeConfigurationCatalogContract.MaximumCandidates)
                .Select(index => new ProcessModeConfigurationCandidate(
                    $"profile-{index}",
                    "server-999999",
                    true,
                    1))
                .ToArray());

        var json = ControlProtocol.SerializeRuntimeConfigCatalog(
            "0123456789abcdef0123456789abcdef",
            result);

        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(json + "\n") <= ControlProtocol.MaxFrameBytes);
    }

    [DataTestMethod]
    [DataRow(ProcessModeConfigurationCatalogFailureReason.CatalogUnavailable, "CatalogUnavailable")]
    [DataRow(ProcessModeConfigurationCatalogFailureReason.CatalogTooLarge, "CatalogTooLarge")]
    public void CatalogFailureReasonEnumIsAClosedSafeTokenSet(
        ProcessModeConfigurationCatalogFailureReason reason,
        string expectedToken)
    {
        var json = ControlProtocol.SerializeRuntimeConfigCatalog(
            "0123456789abcdef0123456789abcdef",
            ProcessModeConfigurationCatalogResult.Failure(reason));

        StringAssert.Contains(json, $"\"reason\":\"{expectedToken}\"");
        Assert.AreEqual(2, Enum.GetValues<ProcessModeConfigurationCatalogFailureReason>().Length);
    }

    [TestMethod]
    public void RuntimeConfigurationValidationResponseSerializesOnlyAllowListedFields()
    {
        var validation = new ProcessModeConfigurationValidation(
            "profile-12",
            "server-3",
            true,
            1,
            true);

        var json = ControlProtocol.SerializeRuntimeConfigValidation(
            "0123456789abcdef0123456789abcdef",
            validation,
            succeeded: true);

        Assert.AreEqual(
            "{\"type\":\"runtimeConfigValidateResponse\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"succeeded\":true,\"profileReference\":\"profile-12\",\"serverReference\":\"server-3\",\"relationshipValid\":true,\"processModeMatchCount\":1,\"valid\":true}",
            json);
    }

    [TestMethod]
    public void RuntimeConfigurationValidationRejectsContradictoryValidityFacts()
    {
        var validation = new ProcessModeConfigurationValidation(
            "profile-12",
            "server-3",
            true,
            1,
            false);

        Assert.ThrowsException<ArgumentException>(() =>
            ControlProtocol.SerializeRuntimeConfigValidation(
                "0123456789abcdef0123456789abcdef",
                validation,
                succeeded: true));
    }

    [DataTestMethod]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 2)]
    public void FailedRuntimeConfigurationValidationRequiresFixedSafeFacts(
        bool relationshipValid,
        int processModeMatchCount)
    {
        var validation = new ProcessModeConfigurationValidation(
            "profile-12",
            "server-3",
            relationshipValid,
            processModeMatchCount,
            false);

        Assert.ThrowsException<ArgumentException>(() =>
            ControlProtocol.SerializeRuntimeConfigValidation(
                "0123456789abcdef0123456789abcdef",
                validation,
                succeeded: false));
    }

    [DataTestMethod]
    [DataRow("runtimeConfigCatalog")]
    [DataRow("runtimeConfigValidate")]
    public void OversizedRuntimeConfigurationRequestsAreRejected(string type)
    {
        var json = "{\"type\":\"" + type + "\",\"correlationId\":\"" +
                   new string('a', ControlProtocol.MaxFrameBytes) + "\"}";

        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out var request, out var error));
        Assert.IsNull(request);
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, error!.ErrorCode);
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
        var start = ValidV3Start("fedcba9876543210fedcba9876543210");
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
    public void InvalidRuntimeConfigurationMapsToApprovedConfigurationMismatchCode()
    {
        var result = ProxyResult.Failure(
            ProxyStatusKind.Failed,
            "0123456789abcdef0123456789abcdef",
            new ProxyError(ProxyErrorCode.InvalidConfiguration, "ignored"));

        var json = ControlProtocol.Serialize(ControlResponse.FromResult(result));

        Assert.IsTrue(json.Contains("\"errorCode\":\"ConfigurationMismatch\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("InvalidConfiguration", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow(ProxyErrorCode.UnsupportedMode)]
    [DataRow(ProxyErrorCode.NotRunning)]
    [DataRow(ProxyErrorCode.Timeout)]
    public void RemainingNonWireRuntimeErrorsStillFailClosedAsUnavailable(ProxyErrorCode code)
    {
        var result = ProxyResult.Failure(
            ProxyStatusKind.Failed,
            "0123456789abcdef0123456789abcdef",
            new ProxyError(code, "ignored"));

        var json = ControlProtocol.Serialize(ControlResponse.FromResult(result));

        Assert.IsTrue(json.Contains("\"errorCode\":\"AuthorizationUnavailable\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(code.ToString(), StringComparison.Ordinal));
    }

    [TestMethod]
    public void WireMapperCoversEveryDefinedErrorCodeWithAnExplicitRegressionPolicy()
    {
        var translated = Enum.GetValues<ProxyErrorCode>()
            .Where(code => code != ProxyErrorCode.AuthorizationUnavailable)
            .Where(code => ControlResponse.FromResult(ProxyResult.Failure(
                    ProxyStatusKind.Failed,
                    "0123456789abcdef0123456789abcdef",
                    new ProxyError(code, "ignored")))
                .ErrorCode == ProxyErrorCode.AuthorizationUnavailable)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                ProxyErrorCode.NotRunning,
                ProxyErrorCode.UnsupportedMode,
                ProxyErrorCode.Timeout
            },
            translated);

        var invalidConfiguration = ControlResponse.FromResult(ProxyResult.Failure(
            ProxyStatusKind.Failed,
            "0123456789abcdef0123456789abcdef",
            new ProxyError(ProxyErrorCode.InvalidConfiguration, "ignored")));
        Assert.AreEqual(ProxyErrorCode.ConfigurationMismatch, invalidConfiguration.ErrorCode);
    }
    [TestMethod]
    public void LauncherStyleProtocolV3StartCarriesExactRuntimeConfig()
    {
        var challenges = new CoreChallengeService();
        challenges.Issue();
        Assert.IsTrue(ControlProtocol.TryParseRequest(ValidV3Start(), challenges, out var request, out var error));
        Assert.IsNull(error);
        Assert.AreEqual(3, ControlProtocol.Version);
        Assert.AreEqual(8192, ControlProtocol.MaxFrameBytes);
        Assert.IsTrue(request!.TryCreateStartRequest(out var start, out error));
        var config = start!.RuntimeConfig!;
        Assert.AreEqual(1, config.SchemaVersion);
        Assert.AreEqual(18L, config.ConfigVersion);
        Assert.AreEqual("japan-vps-1", config.EndpointId);
        Assert.AreEqual("127.0.0.1", config.Host);
        Assert.AreEqual(8389, config.Port);
        Assert.AreEqual("shadowsocks", config.Protocol);
        Assert.AreEqual("aes-256-gcm", config.Cipher);
        Assert.AreEqual("SENTINEL_PROXY_SECRET_42", config.Credential.RevealForTransport());
        Assert.AreEqual(1000L, config.IssuedAt);
        Assert.AreEqual(1120L, config.ExpiresAt);
    }

    [DataTestMethod]
    [DataRow("REMOVE_RUNTIME")]
    [DataRow("NON_OBJECT")]
    [DataRow("DUPLICATE")]
    [DataRow("EXTRA")]
    [DataRow("MISSING")]
    [DataRow("WRONG_SCHEMA")]
    [DataRow("OVERSIZED_CREDENTIAL")]
    [DataRow("CONFIG_BOOL")]
    [DataRow("CONFIG_STRING")]
    [DataRow("CONFIG_UNSAFE")]
    [DataRow("ISSUED_STRING")]
    [DataRow("EXPIRES_BOOL")]
    [DataRow("BAD_LIFETIME")]
    [DataRow("TOP_EXTRA")]
    [DataRow("TOP_MISSING")]
    public void ProtocolV3RejectsMalformedRuntimeConfigOrTopLevelShape(string mutation)
    {
        var json = MutateV3Start(mutation);
        var challenges = new CoreChallengeService();
        challenges.Issue();
        Assert.IsFalse(ControlProtocol.TryParseRequest(json, challenges, out var request, out var error));
        Assert.IsNull(request);
        Assert.AreEqual(ProxyErrorCode.ProtocolInvalid, error!.ErrorCode);
    }

    [TestMethod]
    public void MalformedConfigDoesNotConsumeOutstandingChallenge()
    {
        var challenges = new CoreChallengeService();
        challenges.Issue();
        Assert.IsFalse(ControlProtocol.TryParseRequest(MutateV3Start("WRONG_SCHEMA"), challenges, out _, out _));
        Assert.IsTrue(ControlProtocol.TryParseRequest(ValidV3Start(), challenges, out _, out _));
    }

    [TestMethod]
    public void FrameOver8192BytesIsRejectedWithoutSecretInError()
    {
        var json = ValidV3Start().Replace("header.payload.signature", new string('a', 8200), StringComparison.Ordinal);
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(json) > 8192);
        Assert.IsFalse(ControlProtocol.TryParseRequest(json, out _, out var error));
        Assert.IsFalse(ControlProtocol.Serialize(error!).Contains("SENTINEL_PROXY_SECRET_42", StringComparison.Ordinal));
    }

    private static string ValidV3Start(string correlationId = "0123456789abcdef0123456789abcdef") =>
        "{\"type\":\"start\",\"correlationId\":\"" + correlationId +
        "\",\"protocolVersion\":3,\"mode\":\"ProcessMode\",\"processName\":\"pso2.exe\",\"targetPid\":4242," +
        "\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\"," +
        "\"runtimeConfig\":{\"schemaVersion\":1,\"configVersion\":18,\"endpointId\":\"japan-vps-1\"," +
        "\"host\":\"127.0.0.1\",\"port\":8389,\"protocol\":\"shadowsocks\",\"cipher\":\"aes-256-gcm\"," +
        "\"credential\":\"SENTINEL_PROXY_SECRET_42\",\"issuedAt\":1000,\"expiresAt\":1120}}";

    private static string MutateV3Start(string mutation)
    {
        var json = ValidV3Start();
        return mutation switch
        {
            "REMOVE_RUNTIME" => json.Replace(",\"runtimeConfig\":" + RuntimeObject(), string.Empty, StringComparison.Ordinal),
            "NON_OBJECT" => json.Replace(RuntimeObject(), "[]", StringComparison.Ordinal),
            "DUPLICATE" => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal),
            "EXTRA" => json.Replace("\"expiresAt\":1120", "\"expiresAt\":1120,\"extra\":1", StringComparison.Ordinal),
            "MISSING" => json.Replace("\"endpointId\":\"japan-vps-1\",", string.Empty, StringComparison.Ordinal),
            "WRONG_SCHEMA" => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal),
            "OVERSIZED_CREDENTIAL" => json.Replace("SENTINEL_PROXY_SECRET_42", new string('s', 257), StringComparison.Ordinal),
            "CONFIG_BOOL" => json.Replace("\"configVersion\":18", "\"configVersion\":true", StringComparison.Ordinal),
            "CONFIG_STRING" => json.Replace("\"configVersion\":18", "\"configVersion\":\"18\"", StringComparison.Ordinal),
            "CONFIG_UNSAFE" => json.Replace("\"configVersion\":18", "\"configVersion\":9007199254740992", StringComparison.Ordinal),
            "ISSUED_STRING" => json.Replace("\"issuedAt\":1000", "\"issuedAt\":\"1000\"", StringComparison.Ordinal),
            "EXPIRES_BOOL" => json.Replace("\"expiresAt\":1120", "\"expiresAt\":false", StringComparison.Ordinal),
            "BAD_LIFETIME" => json.Replace("\"expiresAt\":1120", "\"expiresAt\":1119", StringComparison.Ordinal),
            "TOP_EXTRA" => json.Insert(json.Length - 1, ",\"extra\":true"),
            "TOP_MISSING" => json.Replace(",\"permit\":\"header.payload.signature\"", string.Empty, StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }

    private static string RuntimeObject()
    {
        var json = ValidV3Start();
        var marker = "\"runtimeConfig\":";
        return json.Substring(json.IndexOf(marker, StringComparison.Ordinal) + marker.Length, json.Length - json.IndexOf(marker, StringComparison.Ordinal) - marker.Length - 1);
    }
}
