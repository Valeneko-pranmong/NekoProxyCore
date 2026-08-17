using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class TelemetryCompositeSinkTests
{
    [TestMethod]
    public void PrimarySinkAlwaysReceivesStatusEvents()
    {
        var primarySink = new RecordingStatusSink();
        var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var compositeSink = new CompositeProxyStatusSink(primarySink, publisher);

        compositeSink.OnStatusChanged(new ProxyStatusEvent(
            ProxyStatusKind.Starting,
            "corr-1",
            DateTimeOffset.UtcNow));

        compositeSink.OnStatusChanged(new ProxyStatusEvent(
            ProxyStatusKind.Running,
            "corr-1",
            DateTimeOffset.UtcNow));

        Assert.AreEqual(2, primarySink.Events.Count);
        Assert.AreEqual(ProxyStatusKind.Starting, primarySink.Events[0].Status);
        Assert.AreEqual(ProxyStatusKind.Running, primarySink.Events[1].Status);

        Assert.AreEqual(2, buffer.Count);
        Assert.IsTrue(buffer.TryDequeue(out var frame1));
        Assert.IsTrue(buffer.TryDequeue(out var frame2));
        StringAssert.Contains(frame1, "\"message_type\":\"proxy.starting\"");
        StringAssert.Contains(frame2, "\"message_type\":\"proxy.running\"");
    }

    [TestMethod]
    public void TelemetryPublisherFailureDoesNotAffectPrimarySink()
    {
        var primarySink = new RecordingStatusSink();
        var throwingPublisher = new ThrowingPublisher();
        var compositeSink = new CompositeProxyStatusSink(primarySink, throwingPublisher);

        compositeSink.OnStatusChanged(new ProxyStatusEvent(
            ProxyStatusKind.Running,
            "corr-fail",
            DateTimeOffset.UtcNow));

        Assert.AreEqual(1, primarySink.Events.Count);
        Assert.AreEqual(ProxyStatusKind.Running, primarySink.Events[0].Status);
    }

    [TestMethod]
    public void FailedStatusPublishesComponentErrorWithSanitizedCode()
    {
        var primarySink = new RecordingStatusSink();
        var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var compositeSink = new CompositeProxyStatusSink(primarySink, publisher);

        compositeSink.OnStatusChanged(new ProxyStatusEvent(
            ProxyStatusKind.Failed,
            "corr-error",
            DateTimeOffset.UtcNow,
            new ProxyError(ProxyErrorCode.StartFailed, "Detailed raw message that must not leak")));

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        Assert.IsNotNull(frame);
        StringAssert.Contains(frame, "\"message_type\":\"proxy.failed\"");
        StringAssert.Contains(frame, "\"error_code\":\"StartFailed\"");
        // Raw message text must NOT be present in payload
        Assert.IsFalse(frame.Contains("Detailed raw message that must not leak"));
    }

    private sealed class RecordingStatusSink : IProxyStatusSink
    {
        public List<ProxyStatusEvent> Events { get; } = new();

        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
            Events.Add(statusEvent);
        }
    }

    private sealed class ThrowingPublisher : ITelemetryPublisher
    {
        public ulong DroppedEventsCount => 0;

        public void Publish<T>(string messageType, string component, T payload) =>
            throw new InvalidOperationException("Simulated publisher failure.");

        public void PublishLifecycle(string messageType, string component = "core") =>
            throw new InvalidOperationException("Simulated publisher failure.");
    }
}
