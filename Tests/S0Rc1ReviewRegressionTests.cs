using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host.Protocol;

namespace Tests;

[TestClass]
public sealed class S0Rc1ReviewRegressionTests
{
    [TestMethod]
    public void AdmittedStartAtomicallyConsumesChallengeBeforeDispatch()
    {
        const string json = "{\"type\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"protocolVersion\":3,\"mode\":\"ProcessMode\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"header.payload.signature\",\"runtimeConfig\":{\"schemaVersion\":1,\"configVersion\":18,\"endpointId\":\"japan-vps-1\",\"host\":\"127.0.0.1\",\"port\":8389,\"protocol\":\"shadowsocks\",\"cipher\":\"aes-256-gcm\",\"credential\":\"SENTINEL_PROXY_SECRET_42\",\"issuedAt\":1000,\"expiresAt\":1120}}";
        var challenges = new CoreChallengeService();
        var issued = challenges.Issue();

        Assert.IsTrue(ControlProtocol.TryParseRequest(json, challenges, out var request, out _));
        Assert.IsTrue(request!.TryCreateStartRequest(out var startRequest, out _));
        Assert.AreEqual(issued.Value, startRequest!.AdmittedChallenge);
        Assert.AreEqual(ChallengeConsumption.Replayed, challenges.ConsumeForAttempt(issued.Value));
    }

    [TestMethod]
    public void MalformedStartDoesNotConsumeChallengeBeforeAdmission()
    {
        const string json = "{\"version\":2,\"command\":\"start\",\"correlationId\":\"0123456789abcdef0123456789abcdef\",\"processName\":\"pso2.exe\",\"targetPid\":4242,\"mode\":\"ProcessMode\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"permit\":\"not-compact\"}";
        var challenges = new CoreChallengeService();
        var issued = challenges.Issue();

        Assert.IsFalse(ControlProtocol.TryParseRequest(json, challenges, out _, out _));

        Assert.AreEqual(ChallengeConsumption.Accepted, challenges.ConsumeForAttempt(issued.Value));
    }

    [TestMethod]
    public async Task TargetPidMismatchFailsBeforeEngineSideEffect()
    {
        var engine = new CountingEngine();
        var controller = new ProcessModeController(new ExactResolver(false), engine);
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process, "pso2.exe", "profile-0", "server-0", targetPid: 4242);

        var exception = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(() =>
            controller.StartAsync(configuration, CancellationToken.None));

        Assert.AreEqual(ProxyErrorCode.ProcessExited, exception.Code);
        Assert.AreEqual(0, engine.StartCount);
    }

    [TestMethod]
    public async Task TargetPidMismatchFailsBeforeStartingIsPublished()
    {
        var engine = new CountingEngine();
        var sink = new RecordingStatusSink();
        var controller = new ProcessModeController(new ExactResolver(false), engine);
        var runtime = new HeadlessRuntimeCoordinator(controller, new AllowStartAuthorizer(), sink);
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process, "pso2.exe", "profile-0", "server-0", targetPid: 4242);

        var result = await runtime.StartAsync(new ProxyStartRequest(configuration));

        Assert.AreEqual(ProxyErrorCode.ProcessExited, result.Error!.Code);
        Assert.IsFalse(sink.Statuses.Contains(ProxyStatusKind.Starting));
        Assert.AreEqual(0, engine.StartCount);
    }

    [TestMethod]
    public async Task TargetBoundStartFailsClosedWhenExactResolverIsUnavailable()
    {
        var engine = new CountingEngine();
        var sink = new RecordingStatusSink();
        var controller = new ProcessModeController(new NameOnlyResolver(), engine);
        var runtime = new HeadlessRuntimeCoordinator(controller, new AllowStartAuthorizer(), sink);
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process, "pso2.exe", "profile-0", "server-0", targetPid: 4242);

        var result = await runtime.StartAsync(new ProxyStartRequest(configuration));

        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, result.Error!.Code);
        Assert.IsFalse(sink.Statuses.Contains(ProxyStatusKind.Starting));
        Assert.AreEqual(0, engine.StartCount);
    }

    [TestMethod]
    public void CanonicalSerializerRejectsNonContractReferences()
    {
        var serializer = new S0Rc1CanonicalConfigurationSerializer();
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process, "pso2.exe", "fixture-profile", "server-0", targetPid: 4242);

        Assert.ThrowsException<ArgumentException>(() => serializer.Serialize(configuration));
    }

    [TestMethod]
    public void ConfigurationRejectsReferenceWithTrailingNewline()
    {
        Assert.ThrowsException<ArgumentException>(() => new ProxyConfiguration(
            ProxyModeKind.Process, "pso2.exe", "profile-0\n", "server-0", targetPid: 4242));
    }

    [TestMethod]
    public void ExistingProxyErrorNumericValuesRemainStable()
    {
        Assert.AreEqual(7, (int)ProxyErrorCode.AlreadyRunning);
        Assert.AreEqual(15, (int)ProxyErrorCode.Cancelled);
        Assert.AreEqual(0, (int)ControlCommand.Start);
        Assert.AreEqual(1, (int)ControlCommand.Status);
        Assert.AreEqual(2, (int)ControlCommand.Stop);
        Assert.AreEqual(3, (int)ControlCommand.Challenge);
        Assert.AreEqual(4, (int)ControlCommand.Shutdown);
        Assert.AreEqual(5, (int)ControlCommand.RuntimeConfigCatalog);
        Assert.AreEqual(6, (int)ControlCommand.RuntimeConfigValidate);
    }

    private sealed class NameOnlyResolver : IProcessResolver
    {
        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ExactResolver : IProcessResolver, IExactProcessResolver
    {
        private readonly bool _running;
        public ExactResolver(bool running) => _running = running;
        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) => Task.FromResult(_running);
        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> IsExactProcessRunningAsync(string processName, uint targetPid, CancellationToken cancellationToken) => Task.FromResult(_running);
        public Task WaitForExactProcessExitAsync(string processName, uint targetPid, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AllowStartAuthorizer : IProxyStartAuthorizer
    {
        public Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request) => Task.FromResult<ProxyError?>(null);
    }

    private sealed class RecordingStatusSink : IProxyStatusSink
    {
        public System.Collections.Generic.List<ProxyStatusKind> Statuses { get; } = new();
        public void OnStatusChanged(ProxyStatusEvent statusEvent) => Statuses.Add(statusEvent.Status);
    }

    private sealed class CountingEngine : IProcessModeEngine
    {
        public int StartCount { get; private set; }
        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
