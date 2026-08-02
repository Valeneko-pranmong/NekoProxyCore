using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Legacy;

namespace Tests;

[TestClass]
public sealed class LegacyProcessModeEngineTests
{
    [TestMethod]
    public async Task AdapterResolvesOpaqueReferencesAndPublishesTypedLifecycle()
    {
        var session = new FakeSession();
        var resolver = new FakeSessionResolver(session);
        var sink = new RecordingSink();
        var engine = new NetchProcessModeEngine(resolver, sink);
        var configuration = CreateConfiguration();

        await engine.StartAsync(configuration, CancellationToken.None);
        await engine.StopAsync(CancellationToken.None);

        Assert.AreSame(configuration, resolver.Configuration);
        Assert.AreEqual(1, session.StartCount);
        Assert.AreEqual(1, session.StopCount);
        CollectionAssert.AreEqual(
            new[] { ProxyStatusKind.Starting, ProxyStatusKind.Running, ProxyStatusKind.Stopping, ProxyStatusKind.Stopped },
            sink.Events.Select(item => item.Status).ToArray());
    }

    [TestMethod]
    public async Task AdapterMapsLegacyStartErrorsAndCleansUpTheSession()
    {
        var session = new FakeSession
        {
            StartException = new InvalidOperationException("legacy start failure")
        };
        var sink = new RecordingSink();
        var engine = new NetchProcessModeEngine(new FakeSessionResolver(session), sink);

        var exception = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(
            () => engine.StartAsync(CreateConfiguration(), CancellationToken.None));

        Assert.AreEqual(ProxyErrorCode.StartFailed, exception.Code);
        Assert.AreEqual("The legacy ProcessMode engine could not be started.", exception.Message);
        Assert.AreEqual(1, session.StopCount);
        var error = sink.Events.Last(item => item.Status == ProxyStatusKind.Failed).Error;
        Assert.IsNotNull(error);
        Assert.AreEqual("The legacy ProcessMode engine could not be started.", error.SafeMessage);
    }

    [TestMethod]
    public async Task CancelledStartupStopsThePartiallyStartedSession()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeSession { WaitForStartCancellation = true };
        var engine = new NetchProcessModeEngine(new FakeSessionResolver(session));
        var starting = engine.StartAsync(CreateConfiguration(), cancellation.Token);

        await session.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => starting);
        Assert.AreEqual(1, session.StopCount);
    }

    private static ProxyConfiguration CreateConfiguration() =>
        new(ProxyModeKind.Process, "pso2.exe", "profile-0", "server-0");

    private sealed class RecordingSink : IProxyStatusSink
    {
        private readonly List<ProxyStatusEvent> _events = new();

        public IReadOnlyList<ProxyStatusEvent> Events
        {
            get
            {
                lock (_events)
                    return _events.ToArray();
            }
        }

        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
            lock (_events)
                _events.Add(statusEvent);
        }
    }

    private sealed class FakeSessionResolver : ILegacyProcessModeSessionResolver
    {
        private readonly ILegacyProcessModeSession _session;

        public FakeSessionResolver(ILegacyProcessModeSession session) => _session = session;

        public ProxyConfiguration? Configuration { get; private set; }

        public Task<ILegacyProcessModeSession> ResolveAsync(
            ProxyConfiguration configuration,
            IProxyStatusSink statusSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Configuration = configuration;
            return Task.FromResult(_session);
        }
    }

    private sealed class FakeSession : ILegacyProcessModeSession
    {
        public Exception? StartException { get; init; }

        public bool WaitForStartCancellation { get; init; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public TaskCompletionSource<object?> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            StartEntered.TrySetResult(null);
            if (StartException != null)
                throw StartException;
            return WaitForStartCancellation
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }
}
