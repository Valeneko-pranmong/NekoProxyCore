using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class CoreTelemetryAggregatorTests
{
    [TestMethod]
    public async Task AggregatorEmitsSnapshotWithCorrectState()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        Assert.IsNotNull(frame);

        using var doc = JsonDocument.Parse(frame);
        var root = doc.RootElement;

        Assert.AreEqual("core.health.snapshot", root.GetProperty("message_type").GetString());
        var payload = root.GetProperty("payload");
        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("connected", payload.GetProperty("proxy_state").GetString());
        Assert.IsTrue(payload.GetProperty("v2ray_running").GetBoolean());
        Assert.IsTrue(payload.GetProperty("local_socks_running").GetBoolean());
        Assert.IsTrue(payload.GetProperty("shadowsocks_connected").GetBoolean());
        Assert.AreEqual((ulong)0, payload.GetProperty("dropped_telemetry_events").GetUInt64());
    }

    [TestMethod]
    public async Task AggregatorMapsStoppedProxyStateCorrectly()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Stopped);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("stopped", payload.GetProperty("proxy_state").GetString());
        Assert.IsFalse(payload.GetProperty("v2ray_running").GetBoolean());
        Assert.IsFalse(payload.GetProperty("local_socks_running").GetBoolean());
        Assert.IsFalse(payload.GetProperty("shadowsocks_connected").GetBoolean());
    }

    [TestMethod]
    public async Task PeriodicAggregationRunsAndStopsOnCancellation()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource();

        var runTask = aggregator.RunAsync(cts.Token);

        await Task.Delay(150);
        cts.Cancel();
        await runTask;

        Assert.IsTrue(buffer.Count >= 2);
    }

    [TestMethod]
    public async Task AggregatorPopulatesStatisticsFromProvider()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var stats = new NetFilterStatisticsSnapshot(
            TcpConnectTotal: 100,
            TcpActive: 5,
            TcpClosedTotal: 95,
            UdpEventTotal: 50,
            DnsQueryTotal: 30,
            DnsFailureTotal: 1,
            RedirectSuccessTotal: 145,
            RedirectFailureTotal: 5,
            RxBytes: 1048576,
            TxBytes: 524288,
            NetworkErrorTotal: 2);
        var mockProvider = new MockStatsProvider(stats);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, statisticsProvider: mockProvider);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual((ulong)100, payload.GetProperty("tcp_connect_total").GetUInt64());
        Assert.AreEqual((uint)5, payload.GetProperty("tcp_active").GetUInt32());
        Assert.AreEqual((ulong)95, payload.GetProperty("tcp_closed_total").GetUInt64());
        Assert.AreEqual((ulong)50, payload.GetProperty("udp_event_total").GetUInt64());
        Assert.AreEqual((ulong)30, payload.GetProperty("dns_query_total").GetUInt64());
        Assert.AreEqual((ulong)1, payload.GetProperty("dns_failure_total").GetUInt64());
        Assert.AreEqual((ulong)145, payload.GetProperty("redirect_success_total").GetUInt64());
        Assert.AreEqual((ulong)5, payload.GetProperty("redirect_failure_total").GetUInt64());
        Assert.AreEqual((ulong)1048576, payload.GetProperty("rx_bytes").GetUInt64());
        Assert.AreEqual((ulong)524288, payload.GetProperty("tx_bytes").GetUInt64());
        Assert.AreEqual((ulong)2, payload.GetProperty("network_error_total").GetUInt64());
    }

    [TestMethod]
    public async Task AggregatorIsolatesProviderExceptionsAndEmitsSnapshot()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var throwingProvider = new ThrowingStatsProvider();
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, statisticsProvider: throwingProvider);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("connected", payload.GetProperty("proxy_state").GetString());
        Assert.AreEqual((ulong)0, payload.GetProperty("tcp_connect_total").GetUInt64());
    }

    private sealed class MockStatsProvider : INetFilterStatisticsProvider
    {
        private readonly NetFilterStatisticsSnapshot _stats;
        public MockStatsProvider(NetFilterStatisticsSnapshot stats) => _stats = stats;
        public NetFilterStatisticsSnapshot GetCurrentStatistics() => _stats;
    }

    private sealed class ThrowingStatsProvider : INetFilterStatisticsProvider
    {
        public NetFilterStatisticsSnapshot GetCurrentStatistics() =>
            throw new InvalidOperationException("Simulated provider failure");
    }

    private sealed class MockRuntime : IProxyRuntime
    {
        private readonly ProxyStatusKind _status;

        public MockRuntime(ProxyStatusKind status) => _status = status;

        public Task<ProxyResult> StartAsync(ProxyStartRequest request) =>
            Task.FromResult(ProxyResult.Success(_status, request.CorrelationId));

        public Task<ProxyResult> StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProxyResult.Success(ProxyStatusKind.Stopped, string.Empty));

        public Task<ProxyStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProxyStatusSnapshot(_status, string.Empty, DateTimeOffset.UtcNow));
    }
}
