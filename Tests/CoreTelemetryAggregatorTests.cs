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

    [TestMethod]
    public async Task AggregatorPopulatesProxyRttMsFromProducer()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var mockRttProducer = new MockProxyRttProducer(45);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, rttProducer: mockRttProducer);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual(45, payload.GetProperty("proxy_rtt_ms").GetInt32());
    }

    [TestMethod]
    public async Task Aggregator_WhenProducerReturnsZero_EmitsZeroProxyRttMs()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var mockRttProducer = new MockProxyRttProducer(0);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, rttProducer: mockRttProducer);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual(0, payload.GetProperty("proxy_rtt_ms").GetInt32());
    }

    [TestMethod]
    public async Task Aggregator_WhenProducerReturnsNull_EmitsNullProxyRttMsAndPreservesExistingFields()
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
        var mockStats = new MockStatsProvider(stats);
        var mockRttProducer = new MockProxyRttProducer(null);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, statisticsProvider: mockStats, rttProducer: mockRttProducer);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("connected", payload.GetProperty("proxy_state").GetString());
        Assert.AreEqual((ulong)1048576, payload.GetProperty("rx_bytes").GetUInt64());
        Assert.AreEqual((ulong)524288, payload.GetProperty("tx_bytes").GetUInt64());
        Assert.IsTrue(payload.GetProperty("v2ray_running").GetBoolean());
        Assert.IsTrue(payload.GetProperty("local_socks_running").GetBoolean());
        Assert.IsTrue(payload.GetProperty("shadowsocks_connected").GetBoolean());
        Assert.AreEqual((ulong)0, payload.GetProperty("dropped_telemetry_events").GetUInt64());
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("proxy_rtt_ms").ValueKind);
    }

    [TestMethod]
    public async Task AggregatorIsolatesRttProducerExceptionsAndEmitsSnapshot()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var throwingProducer = new ThrowingProxyRttProducer();
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, rttProducer: throwingProducer);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("connected", payload.GetProperty("proxy_state").GetString());
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("proxy_rtt_ms").ValueKind);
    }

    [TestMethod]
    public async Task Aggregator_WhenProducerTimesOutOrCancels_EmitsNullProxyRttMsAndPreservesHealthFields()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var stats = new NetFilterStatisticsSnapshot(
            TcpConnectTotal: 50,
            TcpActive: 2,
            TcpClosedTotal: 48,
            UdpEventTotal: 20,
            DnsQueryTotal: 10,
            DnsFailureTotal: 0,
            RedirectSuccessTotal: 50,
            RedirectFailureTotal: 0,
            RxBytes: 2048,
            TxBytes: 1024,
            NetworkErrorTotal: 0);
        var mockStats = new MockStatsProvider(stats);
        var timeoutProducer = new TimeoutProxyRttProducer();
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, statisticsProvider: mockStats, rttProducer: timeoutProducer);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("connected", payload.GetProperty("proxy_state").GetString());
        Assert.AreEqual((ulong)2048, payload.GetProperty("rx_bytes").GetUInt64());
        Assert.AreEqual((ulong)1024, payload.GetProperty("tx_bytes").GetUInt64());
        Assert.IsTrue(payload.GetProperty("v2ray_running").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("proxy_rtt_ms").ValueKind);
    }

    [TestMethod]
    public async Task Aggregator_WhenProxyNotConnected_DoesNotQueryProducerAndEmitsNullProxyRttMs()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Stopped);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var spyProducer = new SpyProxyRttProducer(50);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, rttProducer: spyProducer);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("stopped", payload.GetProperty("proxy_state").GetString());
        Assert.AreEqual(0, spyProducer.CallCount);
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("proxy_rtt_ms").ValueKind);
    }

    [TestMethod]
    public async Task Aggregator_WhenNoProducerProvided_EmitsNullProxyRttMs()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, rttProducer: null);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual("running", payload.GetProperty("core_state").GetString());
        Assert.AreEqual("connected", payload.GetProperty("proxy_state").GetString());
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("proxy_rtt_ms").ValueKind);
    }

    [TestMethod]
    public async Task Aggregator_ContractProducerConformingToNonNegativeEmitsExpectedNullForNegativeProbeFailure()
    {
        var runtime = new MockRuntime(ProxyStatusKind.Running);
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var contractConformingProducer = new ContractSanitizingMockProducer(-1);
        var aggregator = new CoreTelemetryAggregator(runtime, publisher, rttProducer: contractConformingProducer);

        await aggregator.EmitSnapshotAsync();

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        using var doc = JsonDocument.Parse(frame!);
        var payload = doc.RootElement.GetProperty("payload");

        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("proxy_rtt_ms").ValueKind);
    }

    private sealed class MockProxyRttProducer : IProxyRttProducer
    {
        private readonly int? _rtt;
        public MockProxyRttProducer(int? rtt) => _rtt = rtt;
        public Task<int?> GetRttAsync(CancellationToken cancellationToken = default) => Task.FromResult(_rtt);
    }

    private sealed class ThrowingProxyRttProducer : IProxyRttProducer
    {
        public Task<int?> GetRttAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated RTT producer failure");
    }

    private sealed class TimeoutProxyRttProducer : IProxyRttProducer
    {
        public Task<int?> GetRttAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<int?>(new TimeoutException("Deterministic simulated timeout"));
    }

    private sealed class SpyProxyRttProducer : IProxyRttProducer
    {
        private readonly int? _rtt;
        public int CallCount { get; private set; }
        public SpyProxyRttProducer(int? rtt) => _rtt = rtt;
        public Task<int?> GetRttAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_rtt);
        }
    }

    private sealed class ContractSanitizingMockProducer : IProxyRttProducer
    {
        private readonly int _rawProbeValue;
        public ContractSanitizingMockProducer(int rawProbeValue) => _rawProbeValue = rawProbeValue;
        public Task<int?> GetRttAsync(CancellationToken cancellationToken = default)
        {
            int? rtt = _rawProbeValue >= 0 ? _rawProbeValue : null;
            return Task.FromResult(rtt);
        }
    }
}
