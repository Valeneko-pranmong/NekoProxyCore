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
    public async Task StartAndStopPublishTypedLifecycle()
    {
        var process = new FakeProcessResolver(true);
        var engine = new FakeEngine();
        var sink = new RecordingSink();
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(process, engine), sink);
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
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FakeProcessResolver(true), engine));

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
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FakeProcessResolver(true), engine));
        var request = new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"), "same-session");

        var first = await runtime.StartAsync(request);
        var second = await runtime.StartAsync(request);

        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(ProxyErrorCode.AlreadyRunning, second.Error!.Code);
        Assert.AreEqual(1, engine.StartCount);
    }

    [TestMethod]
    public async Task InvalidProcessReturnsTypedErrorWithoutStartingEngine()
    {
        var engine = new FakeEngine();
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FakeProcessResolver(false), engine));
        var request = new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"), "invalid-process");

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.ProcessNotFound, result.Error!.Code);
        Assert.AreEqual(0, engine.StartCount);
    }

    [TestMethod]
    public async Task ProcessExitDuringStartReturnsTypedError()
    {
        var engine = new FakeEngine();
        var resolver = new SequenceProcessResolver(true, false);
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(resolver, engine));
        var request = new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server"));

        var result = await runtime.StartAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.ProcessExited, result.Error!.Code);
        Assert.AreEqual(1, engine.StartCount);
    }

    [TestMethod]
    public async Task ProcessExitAfterStartupStopsTheEngineAndRuntime()
    {
        var process = new ExitSignalProcessResolver();
        var engine = new FakeEngine();
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(process, engine));
        var configuration = new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server");

        Assert.IsTrue((await runtime.StartAsync(new ProxyStartRequest(configuration, "process-exit"))).Succeeded);
        process.SignalExit();

        await engine.StopCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(ProxyStatusKind.Stopped, (await runtime.GetStatusAsync()).Status);
        Assert.AreEqual(1, engine.StopCount);
    }

    [TestMethod]
    public async Task ProcessExitMonitorFailureStopsTheEngineAndReturnsTypedStatus()
    {
        var engine = new FakeEngine();
        var sink = new FailureRecordingSink();
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FailingExitProcessResolver(), engine), sink);
        var configuration = new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server");

        Assert.IsTrue((await runtime.StartAsync(new ProxyStartRequest(configuration, "process-monitor-failure"))).Succeeded);

        await sink.Failed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var status = await runtime.GetStatusAsync();
        Assert.AreEqual(ProxyStatusKind.Failed, status.Status);
        Assert.AreEqual(ProxyErrorCode.StartFailed, status.Error!.Code);
        Assert.AreEqual(1, engine.StopCount);
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
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FakeProcessResolver(true), engine));
        var config = new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server", TimeSpan.FromMilliseconds(30));

        var result = await runtime.StartAsync(new ProxyStartRequest(config, "timeout-session"));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.Timeout, result.Error!.Code);
        Assert.AreEqual(ProxyStatusKind.Failed, (await runtime.GetStatusAsync()).Status);
    }

    [TestMethod]
    public async Task StopTimeoutUsesTheConfigurationTimeout()
    {
        var engine = new FakeEngine { StopDelay = TimeSpan.FromSeconds(2) };
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FakeProcessResolver(true), engine));
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
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FakeProcessResolver(true), new FakeEngine()));
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
        var runtime = new HeadlessRuntimeCoordinator(new ProcessModeController(new FakeProcessResolver(true), engine));
        var result = await runtime.StartAsync(new ProxyStartRequest(new ProxyConfiguration(ProxyModeKind.Process, "pso2.exe", "fixture-pso2", "fixture-server")));

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Error!.SafeMessage.Contains("sentinel-token", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.SafeMessage.Contains("another-secret", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.SafeMessage.Contains("uri-secret", StringComparison.Ordinal));
        StringAssert.Contains(result.Error.SafeMessage, "[REDACTED]");
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
            }
            finally
            {
                StopCompleted.TrySetResult(null);
            }
        }
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
