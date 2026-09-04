using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Windows;

namespace Tests;

[TestClass]
public sealed class HeadlessRuntimeTests
{
    [TestMethod]
    public async Task RuntimeConfigIsForwardedByExactIdentity()
    {
        var controller = new RuntimeConfiguredController();
        var runtime = CreateAuthorizedRuntime(controller);
        var config = CreateRuntimeConfig();
        var result = await runtime.StartAsync(new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"), runtimeConfig: config));
        Assert.IsTrue(result.Succeeded);
        Assert.AreSame(config, controller.RuntimeConfig);
        Assert.AreEqual(0, controller.LegacyStartCount);
    }

    [TestMethod]
    public async Task RuntimeConfigRequiresCapableControllerBeforeStartingIsPublished()
    {
        var controller = new PartialStartFailingController();
        var sink = new RecordingSink();
        var runtime = CreateAuthorizedRuntime(controller, sink);
        var result = await runtime.StartAsync(new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"), runtimeConfig: CreateRuntimeConfig()));
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, result.Error!.Code);
        Assert.AreEqual(0, controller.StartCount);
        Assert.IsFalse(sink.Events.Any(e => e.Status == ProxyStatusKind.Starting));
    }

    private static RuntimeProxyConfig CreateRuntimeConfig() => new(
        1, 1, "endpoint", "example.invalid", 443, "shadowsocks", "aes-256-gcm",
        new SensitiveRuntimeCredential("secret"), 1000, 1120);
    [TestMethod]
    public async Task StartAndStopPublishTypedLifecycle()
    {
        var process = new FakeProcessResolver(true);
        var engine = new FakeEngine();
        var sink = new RecordingSink();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(process, engine), sink);
        var request = new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"), "test-session");

        var started = await runtime.StartAsync(request);
        var stopped = await runtime.StopAsync();

        Assert.IsTrue(started.Succeeded);
        Assert.AreEqual(ProxyStatusKind.Running, started.Status);
        Assert.IsTrue(stopped.Succeeded);
        CollectionAssert.AreEqual(
            new[] { ProxyStatusKind.Starting, ProxyStatusKind.Running, ProxyStatusKind.Stopping, ProxyStatusKind.Stopped },
            sink.Events.Select(x => x.Status).ToArray());
        Assert.AreEqual(1, engine.StartCount);
        Assert.AreEqual(1, engine.StopCount);
    }

    [TestMethod]
    public async Task RepeatedStopIsIdempotent()
    {
        var engine = new FakeEngine();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), engine));

        var first = await runtime.StopAsync();
        var second = await runtime.StopAsync();

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(second.Succeeded);
        Assert.AreEqual(0, engine.StopCount);
    }

    [TestMethod]
    public async Task RepeatedStartReturnsAlreadyRunningWithoutCallingEngineAgain()
    {
        var engine = new FakeEngine();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), engine));
        var request = new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"), "same-session");

        var first = await runtime.StartAsync(request);
        var second = await runtime.StartAsync(request);

        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(ProxyErrorCode.AlreadyRunning, second.Error!.Code);
        Assert.AreEqual(1, engine.StartCount);
    }

    [TestMethod]
    public async Task MissingAuthorizationReturnsTypedErrorWithoutStartingEngine()
    {
        var engine = new FakeEngine();
        var runtime = new HeadlessRuntimeCoordinator(
            new ProcessModeController(new FakeProcessResolver(true), engine));
        var request = new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"),
            "authorization-required");

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.AuthorizationRequired, result.Error!.Code);
        Assert.AreEqual(0, engine.StartCount);
    }

    [TestMethod]
    public async Task AuthorizerExceptionReturnsSanitizedUnavailableWithoutStartingEngine()
    {
        const string sentinel = "raw-authorizer-secret-sentinel";
        var engine = new FakeEngine();
        var sink = new RecordingSink();
        var runtime = new HeadlessRuntimeCoordinator(
            new ProcessModeController(new FakeProcessResolver(true), engine),
            new ThrowingStartAuthorizer(sentinel),
            sink);
        var request = new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"),
            "authorization-unavailable");

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.AuthorizationUnavailable, result.Error!.Code);
        Assert.AreEqual("Online authorization is unavailable.", result.Error.SafeMessage);
        Assert.IsFalse(result.Error.SafeMessage.Contains(sentinel, StringComparison.Ordinal));
        Assert.AreEqual(0, engine.StartCount);
        Assert.IsFalse(sink.Events.Any(x => x.Status == ProxyStatusKind.Starting));
        Assert.IsFalse(sink.Events.Any(x => x.Error?.SafeMessage.Contains(sentinel, StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public async Task InvalidProcessReturnsTypedErrorWithoutStartingEngine()
    {
        var engine = new FakeEngine();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(false), engine));
        var request = new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"), "invalid-process");

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.ProcessNotFound, result.Error!.Code);
        Assert.AreEqual(0, engine.StartCount);
    }

    [TestMethod]
    public async Task PartialModeStartFailureCleansUpBeforeReturningFailure()
    {
        var controller = new PartialStartFailingController();
        var runtime = CreateAuthorizedRuntime(controller);
        var request = new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"),
            "partial-start-cleanup");

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.StartFailed, result.Error!.Code);
        Assert.AreEqual(1, controller.StartCount);
        Assert.AreEqual(1, controller.StopCount);
        Assert.IsFalse(controller.HasOwnedState);
    }

    [TestMethod]
    public async Task PartialModeStartCleanupFailurePreservesStartCauseAndRetainsOwnershipUntilStopRetries()
    {
        var controller = new PartialStartFailingController { StopFailuresRemaining = 1 };
        var runtime = CreateAuthorizedRuntime(controller);
        var request = new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"),
            "partial-start-cleanup-failure");

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.StartFailed, result.Error!.Code);
        Assert.AreEqual("Proxy start failed.", result.Error.SafeMessage);
        Assert.AreEqual(1, controller.StopCount);
        Assert.IsTrue(controller.HasOwnedState);

        var duplicateStart = await runtime.StartAsync(new ProxyStartRequest(request.Configuration, "blocked-duplicate-start"));

        Assert.IsFalse(duplicateStart.Succeeded);
        Assert.AreEqual(ProxyErrorCode.AlreadyRunning, duplicateStart.Error!.Code);
        Assert.AreEqual(1, controller.StartCount);

        var stopped = await runtime.StopAsync();

        Assert.IsTrue(stopped.Succeeded);
        Assert.AreEqual(ProxyStatusKind.Stopped, stopped.Status);
        Assert.AreEqual(2, controller.StopCount);
        Assert.IsFalse(controller.HasOwnedState);
    }

    [TestMethod]
    public async Task ProcessExitDuringStartReturnsTypedError()
    {
        var engine = new FakeEngine();
        var resolver = new SequenceProcessResolver(true, true, false);
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(resolver, engine));
        var request = new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"));

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.ProcessExited, result.Error!.Code);
        Assert.AreEqual(1, engine.StartCount);
        Assert.AreEqual(1, engine.StopCount);
    }

    [TestMethod]
    public async Task ProcessExitAfterStartupStopsTheEngineAndRuntime()
    {
        var process = new ExitSignalProcessResolver();
        var engine = new FakeEngine();
        var sink = new StoppedRecordingSink();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(process, engine), sink);
        var configuration = new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server");

        Assert.IsTrue((await runtime.StartAsync(new ProxyStartRequest(configuration, "process-exit"))).Succeeded);
        process.SignalExit();

        await sink.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(ProxyStatusKind.Stopped, (await runtime.GetStatusAsync()).Status);
        Assert.AreEqual(1, engine.StopCount);
    }

    [TestMethod]
    public async Task ProcessExitMonitorFailureStopsTheEngineAndReturnsTypedStatus()
    {
        var engine = new FakeEngine();
        var sink = new FailureRecordingSink();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FailingExitProcessResolver(), engine), sink);
        var configuration = new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server");

        Assert.IsTrue((await runtime.StartAsync(new ProxyStartRequest(configuration, "process-monitor-failure"))).Succeeded);

        await sink.Failed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var status = await runtime.GetStatusAsync();
        Assert.AreEqual(ProxyStatusKind.Failed, status.Status);
        Assert.AreEqual(ProxyErrorCode.StartFailed, status.Error!.Code);
        Assert.AreEqual(1, engine.StopCount);

        var stopped = await runtime.StopAsync();

        Assert.IsTrue(stopped.Succeeded);
        Assert.AreEqual(ProxyStatusKind.Stopped, stopped.Status);
        Assert.AreEqual(1, engine.StopCount);
    }

    [TestMethod]
    public async Task TypedMonitorExceptionReturnsAllowListedMessage()
    {
        const string sentinel = "typed-monitor-secret-sentinel";
        var sink = new FailureRecordingSink();
        var controller = new TypedFailingExitController(
            new ProxyRuntimeException(ProxyErrorCode.ProcessExited, sentinel));
        var runtime = CreateAuthorizedRuntime(controller, sink);
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process,
            "pso2.exe",
            "fixture-pso2",
            "fixture-server");

        Assert.IsTrue((await runtime.StartAsync(
            new ProxyStartRequest(configuration, "typed-monitor-failure"))).Succeeded);

        var failure = await sink.Failed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(ProxyErrorCode.ProcessExited, failure.Error!.Code);
        Assert.AreEqual("The target process exited during startup.", failure.Error.SafeMessage);
        Assert.IsFalse(failure.Error.SafeMessage.Contains(sentinel, StringComparison.Ordinal));
        Assert.AreEqual(1, controller.StopCount);
    }

    [TestMethod]
    public async Task MonitorCleanupFailureRetainsOwnershipUntilExplicitStopSucceeds()
    {
        var engine = new FakeEngine { StopFailuresRemaining = 1 };
        var sink = new FailureRecordingSink();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FailingExitProcessResolver(), engine), sink);
        var configuration = new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server");

        Assert.IsTrue((await runtime.StartAsync(new ProxyStartRequest(configuration, "monitor-cleanup-failure"))).Succeeded);

        var failure = await sink.Failed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(ProxyErrorCode.StopFailed, failure.Error!.Code);
        Assert.AreEqual("Proxy cleanup failed.", failure.Error.SafeMessage);
        Assert.AreEqual(1, engine.StopCount);

        var stopped = await runtime.StopAsync();

        Assert.IsTrue(stopped.Succeeded);
        Assert.AreEqual(ProxyStatusKind.Stopped, stopped.Status);
        Assert.AreEqual(2, engine.StopCount);
    }

    [TestMethod]
    public void InvalidConfigurationCanBeReturnedAsSafeTypedResult()
    {
        var created = ProxyConfiguration.TryCreate(
            ProxyModeKind.Process,
            string.Empty,
            "fixture-pso2",
            "fixture-server",
            null,
            null,
            out var configuration,
            out var error);

        Assert.IsFalse(created);
        Assert.IsNull(configuration);
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, error!.Code);
        Assert.AreEqual("Proxy configuration is invalid.", error.SafeMessage);
    }

    [TestMethod]
    public void SecretLikeReferencesAreRejectedBeforeTheRuntimeBoundary()
    {
        var created = ProxyConfiguration.TryCreate(
            ProxyModeKind.Process,
            "pso2.exe",
            "fixture-pso2",
            "password=sentinel-token",
            null,
            null,
            out var configuration,
            out var error);

        Assert.IsFalse(created);
        Assert.IsNull(configuration);
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, error!.Code);
    }

    [TestMethod]
    public async Task StartTimeoutIsTypedAndSafe()
    {
        var engine = new FakeEngine { StartDelay = TimeSpan.FromSeconds(2) };
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), engine));
        var config = new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server", TimeSpan.FromMilliseconds(30));

        var result = await runtime.StartAsync(new ProxyStartRequest(config, "timeout-session"));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.StartTimeout, result.Error!.Code);
        Assert.AreEqual(ProxyStatusKind.Failed, (await runtime.GetStatusAsync()).Status);
    }

    [TestMethod]
    public async Task NonCooperativeTimedOutStartIsCleanedOnlyAfterItCompletes()
    {
        var controller = new LateCompletingStartController();
        var runtime = CreateAuthorizedRuntime(controller);
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process,
            "pso2.exe",
            "fixture-pso2",
            "fixture-server",
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromSeconds(1));

        var result = await runtime.StartAsync(new ProxyStartRequest(configuration, "late-start"));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.StartTimeout, result.Error!.Code);
        Assert.AreEqual(0, controller.StopCount);

        var duplicate = await runtime.StartAsync(new ProxyStartRequest(configuration, "late-start-duplicate"));
        Assert.IsFalse(duplicate.Succeeded);
        Assert.AreEqual(ProxyErrorCode.AlreadyRunning, duplicate.Error!.Code);
        Assert.AreEqual(1, controller.StartCount);

        controller.ReleaseStart();
        await controller.StopCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, controller.StopCount);
        Assert.IsFalse(controller.HasOwnedState);
    }

    [TestMethod]
    public async Task LateStartCleanupFailurePreservesTimeoutAndExplicitStopRetriesOwnership()
    {
        var controller = new LateCompletingStartController { StopFailuresRemaining = 1 };
        var runtime = CreateAuthorizedRuntime(controller);
        var configuration = new ProxyConfiguration(
            ProxyModeKind.Process,
            "pso2.exe",
            "fixture-pso2",
            "fixture-server",
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromSeconds(1));

        var result = await runtime.StartAsync(new ProxyStartRequest(configuration, "late-cleanup-failure"));
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.StartTimeout, result.Error!.Code);

        controller.ReleaseStart();
        await controller.StopAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var status = await runtime.GetStatusAsync();
        Assert.AreEqual(ProxyStatusKind.Failed, status.Status);
        Assert.AreEqual(ProxyErrorCode.StartTimeout, status.Error!.Code);
        Assert.AreEqual(1, controller.StopCount);
        Assert.IsTrue(controller.HasOwnedState);

        var duplicate = await runtime.StartAsync(new ProxyStartRequest(configuration, "late-cleanup-duplicate"));
        Assert.IsFalse(duplicate.Succeeded);
        Assert.AreEqual(ProxyErrorCode.AlreadyRunning, duplicate.Error!.Code);

        var stopped = await runtime.StopAsync();
        Assert.IsTrue(stopped.Succeeded);
        Assert.AreEqual(2, controller.StopCount);
        Assert.IsFalse(controller.HasOwnedState);
    }

    [TestMethod]
    public async Task StopTimeoutUsesTheConfigurationTimeout()
    {
        var engine = new FakeEngine { StopDelay = TimeSpan.FromSeconds(2) };
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), engine));
        var config = new ProxyConfiguration(
            ProxyModeKind.Process,
            "pso2.exe",
            "fixture-pso2",
            "fixture-server",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(30));

        Assert.IsTrue((await runtime.StartAsync(new ProxyStartRequest(config, "stop-timeout"))).Succeeded);
        var result = await runtime.StopAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.Timeout, result.Error!.Code);
    }

    [TestMethod]
    public async Task CancelledStartReturnsTypedResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), new FakeEngine()));
        var request = new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"),
            "cancelled-start",
            cancellation.Token);

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.Cancelled, result.Error!.Code);
        Assert.AreEqual(ProxyStatusKind.Stopped, result.Status);
    }

    [TestMethod]
    public async Task ErrorMessagesRedactSecretAssignments()
    {
        var engine = new FakeEngine { Exception = new InvalidOperationException("connect failed password=sentinel-token --token another-secret https://user:uri-secret@example.test") };
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), engine));
        var result = await runtime.StartAsync(new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server")));

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Error!.SafeMessage.Contains("sentinel-token", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.SafeMessage.Contains("another-secret", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.SafeMessage.Contains("uri-secret", StringComparison.Ordinal));
        Assert.AreEqual("Proxy start failed.", result.Error.SafeMessage);
    }

    [TestMethod]
    public async Task UnexpectedStartExceptionReturnsAllowListedMessage()
    {
        const string sentinel = "raw-runtime-detail-sentinel";
        var engine = new FakeEngine { Exception = new InvalidOperationException(sentinel) };
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), engine));

        var result = await runtime.StartAsync(new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server")));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.StartFailed, result.Error!.Code);
        Assert.AreEqual("Proxy start failed.", result.Error.SafeMessage);
        Assert.IsFalse(result.Error.SafeMessage.Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TypedStartExceptionReturnsAllowListedMessage()
    {
        const string sentinel = "typed-start-secret-sentinel";
        var controller = new ThrowingTypedController(
            new ProxyRuntimeException(ProxyErrorCode.ProcessExited, sentinel));
        var runtime = CreateAuthorizedRuntime(controller);

        var result = await runtime.StartAsync(new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server")));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.ProcessExited, result.Error!.Code);
        Assert.AreEqual("The target process exited during startup.", result.Error.SafeMessage);
        Assert.IsFalse(result.Error.SafeMessage.Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UnexpectedStopExceptionReturnsAllowListedMessage()
    {
        const string sentinel = "raw-stop-detail-sentinel";
        var engine = new FakeEngine { StopException = new InvalidOperationException(sentinel) };
        var runtime = CreateAuthorizedRuntime(new ProcessModeController(new FakeProcessResolver(true), engine));
        Assert.IsTrue((await runtime.StartAsync(new ProxyStartRequest(
            new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server")))).Succeeded);

        var result = await runtime.StopAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.StopFailed, result.Error!.Code);
        Assert.AreEqual("Proxy stop failed.", result.Error.SafeMessage);
        Assert.IsFalse(result.Error.SafeMessage.Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void CoreAssemblyDoesNotReferenceWinForms()
    {
        var references = typeof(HeadlessRuntimeCoordinator).Assembly.GetReferencedAssemblies();
        Assert.IsFalse(references.Any(x => x.Name is "System.Windows.Forms" or "WindowsBase"));
    }

    [TestMethod]
    public async Task WindowsResolverFindsTheCurrentProcessWithOrWithoutExeSuffix()
    {
        var resolver = new WindowsProcessResolver();
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var processName = current.ProcessName;

        Assert.IsTrue(await resolver.IsRunningAsync(processName, CancellationToken.None));
        Assert.IsTrue(await resolver.IsRunningAsync(processName + ".exe", CancellationToken.None));
    }

    [TestMethod]
    public async Task WindowsResolverHonorsCancellationWhileWaiting()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var resolver = new WindowsProcessResolver();
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var processName = current.ProcessName;

        try
        {
            await resolver.WaitForExitAsync(processName, cancellation.Token);
            Assert.Fail("Expected process wait to be cancelled.");
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException is the expected cancellation subtype from Task.WaitAsync.
        }
    }

    [TestMethod]
    public async Task WindowsResolverCanObserveProtectedProcessWithoutFailingStartup()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var resolver = new WindowsProcessResolver();

        try
        {
            await resolver.WaitForExitAsync("System", cancellation.Token);
            Assert.Fail("Expected protected process wait to be cancelled.");
        }
        catch (OperationCanceledException)
        {
            // A protected process cannot always expose a wait handle. The fallback must remain
            // active until cancellation instead of failing the runtime monitor.
        }
    }

    private static HeadlessRuntimeCoordinator CreateAuthorizedRuntime(
        IProxyModeController modeController,
        IProxyStatusSink? statusSink = null) =>
        new(modeController, new TestStartAuthorizer(), statusSink);

    private sealed class TestStartAuthorizer : IProxyStartAuthorizer
    {
        public Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request) => Task.FromResult<ProxyError?>(null);
    }

    private sealed class ThrowingStartAuthorizer : IProxyStartAuthorizer
    {
        private readonly string _message;

        public ThrowingStartAuthorizer(string message) => _message = message;

        public Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request) =>
            Task.FromException<ProxyError?>(new InvalidOperationException(_message));
    }

    private sealed class RecordingSink : IProxyStatusSink
    {
        public List<ProxyStatusEvent> Events { get; } = new();

        public void OnStatusChanged(ProxyStatusEvent statusEvent) => Events.Add(statusEvent);
    }

    private sealed class FakeProcessResolver : IProcessResolver
    {
        private readonly bool _running;

        public FakeProcessResolver(bool running) => _running = running;

        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) => Task.FromResult(_running);

        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FakeEngine : IProcessModeEngine
    {
        public TimeSpan StartDelay { get; init; }

        public TimeSpan StopDelay { get; init; }

        public Exception? Exception { get; init; }

        public Exception? StopException { get; init; }

        public int StopFailuresRemaining { get; set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public TaskCompletionSource<object?> StopCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
        {
            StartCount++;
            if (StartDelay > TimeSpan.Zero)
                await Task.Delay(StartDelay, cancellationToken);
            if (Exception is not null)
                throw Exception;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return StopAsyncCore(cancellationToken);
        }

        private async Task StopAsyncCore(CancellationToken cancellationToken)
        {
            try
            {
                if (StopDelay > TimeSpan.Zero)
                    await Task.Delay(StopDelay, cancellationToken);
                if (StopFailuresRemaining > 0)
                {
                    StopFailuresRemaining--;
                    throw new InvalidOperationException("transient stop failure sentinel");
                }
                if (StopException is not null)
                    throw StopException;
            }
            finally
            {
                StopCompleted.TrySetResult(null);
            }
        }
    }

    private sealed class RuntimeConfiguredController : IProxyModeController, IRuntimeConfiguredProxyModeController
    {
        public int LegacyStartCount { get; private set; }
        public RuntimeProxyConfig? RuntimeConfig { get; private set; }
        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
        { LegacyStartCount++; return Task.CompletedTask; }
        public Task StartAsync(ProxyConfiguration configuration, RuntimeProxyConfig runtimeConfig, CancellationToken cancellationToken)
        { RuntimeConfig = runtimeConfig; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SequenceProcessResolver : IProcessResolver
    {
        private readonly Queue<bool> _states;

        public SequenceProcessResolver(params bool[] states) => _states = new Queue<bool>(states);

        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken)
        {
            if (_states.Count == 0)
                return Task.FromResult(false);

            return Task.FromResult(_states.Dequeue());
        }

        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class LateCompletingStartController : IProxyModeController
    {
        private readonly TaskCompletionSource<object?> _releaseStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int StopFailuresRemaining { get; init; }

        public bool HasOwnedState { get; private set; }

        public TaskCompletionSource<object?> StopCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> StopAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
        {
            StartCount++;
            await _releaseStart.Task.ConfigureAwait(false);
            HasOwnedState = true;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            StopAttempted.TrySetResult(null);
            if (StopFailuresRemaining >= StopCount)
                throw new InvalidOperationException("late cleanup failure sentinel");
            HasOwnedState = false;
            StopCompleted.TrySetResult(null);
            return Task.CompletedTask;
        }

        public void ReleaseStart() => _releaseStart.TrySetResult(null);
    }

    private sealed class PartialStartFailingController : IProxyModeController
    {
        public int StopFailuresRemaining { get; set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool HasOwnedState { get; private set; }

        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
        {
            StartCount++;
            HasOwnedState = true;
            throw new InvalidOperationException("partial start failure sentinel");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            if (StopFailuresRemaining > 0)
            {
                StopFailuresRemaining--;
                throw new InvalidOperationException("partial cleanup failure sentinel");
            }

            HasOwnedState = false;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTypedController : IProxyModeController
    {
        private readonly ProxyRuntimeException _exception;

        public ThrowingTypedController(ProxyRuntimeException exception) => _exception = exception;

        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken) =>
            Task.FromException(_exception);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TypedFailingExitController : IProxyModeController, IProcessExitWatcher
    {
        private readonly ProxyRuntimeException _exception;

        public TypedFailingExitController(ProxyRuntimeException exception) => _exception = exception;

        public int StopCount { get; private set; }

        public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task WaitForProcessExitAsync(
            ProxyConfiguration configuration,
            CancellationToken cancellationToken) => Task.FromException(_exception);
    }

    private sealed class ExitSignalProcessResolver : IProcessResolver
    {
        private readonly TaskCompletionSource<object?> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) => _exited.Task.WaitAsync(cancellationToken);

        public void SignalExit() => _exited.TrySetResult(null);
    }

    private sealed class FailingExitProcessResolver : IProcessResolver
    {
        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("process observer unavailable"));
    }

    private sealed class StoppedRecordingSink : IProxyStatusSink
    {
        public TaskCompletionSource<ProxyStatusEvent> Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
            if (statusEvent.Status == ProxyStatusKind.Stopped)
                Stopped.TrySetResult(statusEvent);
        }
    }

    private sealed class FailureRecordingSink : IProxyStatusSink
    {
        public TaskCompletionSource<ProxyStatusEvent> Failed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
            if (statusEvent.Status == ProxyStatusKind.Failed)
                Failed.TrySetResult(statusEvent);
        }
    }
}
