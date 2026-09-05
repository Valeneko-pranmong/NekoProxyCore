using System;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class TelemetryMessageSerializationTests
{
    [TestMethod]
    public void EnvelopeSerializesWithSchemaVersion1AndStandardFields()
    {
        var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer, () => new DateTimeOffset(2026, 8, 17, 2, 30, 0, TimeSpan.Zero));

        publisher.Publish("core.started", "core", new { status = "ok" });

        Assert.IsTrue(buffer.TryDequeue(out var frame));
        Assert.IsNotNull(frame);

        using var doc = JsonDocument.Parse(frame);
        var root = doc.RootElement;

        Assert.AreEqual(1, root.GetProperty("schema_version").GetInt32());
        Assert.AreEqual((ulong)1, root.GetProperty("sequence").GetUInt64());
        Assert.AreEqual("2026-08-17T02:30:00.000Z", root.GetProperty("timestamp_utc").GetString());
        Assert.AreEqual("core.started", root.GetProperty("message_type").GetString());
        Assert.AreEqual("core", root.GetProperty("component").GetString());
        Assert.AreEqual("ok", root.GetProperty("payload").GetProperty("status").GetString());
    }

    [TestMethod]
    public void SequenceIncrementsMonotonically()
    {
        var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);

        publisher.PublishLifecycle("event.first");
        publisher.PublishLifecycle("event.second");
        publisher.PublishLifecycle("event.third");

        Assert.IsTrue(buffer.TryDequeue(out var frame1));
        Assert.IsTrue(buffer.TryDequeue(out var frame2));
        Assert.IsTrue(buffer.TryDequeue(out var frame3));

        using var doc1 = JsonDocument.Parse(frame1!);
        using var doc2 = JsonDocument.Parse(frame2!);
        using var doc3 = JsonDocument.Parse(frame3!);

        Assert.AreEqual((ulong)1, doc1.RootElement.GetProperty("sequence").GetUInt64());
        Assert.AreEqual((ulong)2, doc2.RootElement.GetProperty("sequence").GetUInt64());
        Assert.AreEqual((ulong)3, doc3.RootElement.GetProperty("sequence").GetUInt64());
    }

    [TestMethod]
    public void HealthSnapshotSerializesContractCompliantFields()
    {
        var payload = new CoreHealthSnapshotPayload(
            CoreState: "running",
            ProxyState: "connected",
            UptimeMs: 125000,
            TcpConnectTotal: 412,
            TcpActive: 8,
            TcpClosedTotal: 404,
            UdpEventTotal: 95,
            DnsQueryTotal: 64,
            DnsFailureTotal: 0,
            RedirectSuccessTotal: 412,
            RedirectFailureTotal: 0,
            RxBytes: 154820912,
            TxBytes: 12490184,
            NetworkErrorTotal: 0,
            V2RayRunning: true,
            LocalSocksRunning: true,
            ShadowsocksConnected: true,
            DroppedTelemetryEvents: 0,
            ProxyRttMs: 42);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("running", root.GetProperty("core_state").GetString());
        Assert.AreEqual("connected", root.GetProperty("proxy_state").GetString());
        Assert.AreEqual((ulong)125000, root.GetProperty("uptime_ms").GetUInt64());
        Assert.AreEqual((ulong)412, root.GetProperty("tcp_connect_total").GetUInt64());
        Assert.AreEqual((uint)8, root.GetProperty("tcp_active").GetUInt32());
        Assert.AreEqual((ulong)404, root.GetProperty("tcp_closed_total").GetUInt64());
        Assert.AreEqual((ulong)95, root.GetProperty("udp_event_total").GetUInt64());
        Assert.AreEqual((ulong)64, root.GetProperty("dns_query_total").GetUInt64());
        Assert.AreEqual((ulong)0, root.GetProperty("dns_failure_total").GetUInt64());
        Assert.AreEqual((ulong)412, root.GetProperty("redirect_success_total").GetUInt64());
        Assert.AreEqual((ulong)0, root.GetProperty("redirect_failure_total").GetUInt64());
        Assert.AreEqual((ulong)154820912, root.GetProperty("rx_bytes").GetUInt64());
        Assert.AreEqual((ulong)12490184, root.GetProperty("tx_bytes").GetUInt64());
        Assert.AreEqual((ulong)0, root.GetProperty("network_error_total").GetUInt64());
        Assert.IsTrue(root.GetProperty("v2ray_running").GetBoolean());
        Assert.IsTrue(root.GetProperty("local_socks_running").GetBoolean());
        Assert.IsTrue(root.GetProperty("shadowsocks_connected").GetBoolean());
        Assert.AreEqual((ulong)0, root.GetProperty("dropped_telemetry_events").GetUInt64());
        Assert.AreEqual(42, root.GetProperty("proxy_rtt_ms").GetInt32());
    }

    [TestMethod]
    public void DeserializationIsLenientToAdditiveFields()
    {
        const string jsonWithExtra = "{\"schema_version\":1,\"sequence\":42,\"timestamp_utc\":\"2026-08-17T02:30:00.000Z\",\"message_type\":\"core.health.snapshot\",\"component\":\"core\",\"payload\":{\"core_state\":\"running\",\"proxy_state\":\"connected\",\"uptime_ms\":5000,\"v2ray_running\":true,\"local_socks_running\":true,\"shadowsocks_connected\":true,\"dropped_telemetry_events\":0,\"future_t2_metric\":999}}";

        var envelope = JsonSerializer.Deserialize<TelemetryEnvelope<CoreHealthSnapshotPayload>>(jsonWithExtra);

        Assert.IsNotNull(envelope);
        Assert.AreEqual(1, envelope.SchemaVersion);
        Assert.AreEqual((ulong)42, envelope.Sequence);
        Assert.AreEqual("core.health.snapshot", envelope.MessageType);
        Assert.AreEqual("running", envelope.Payload.CoreState);
        Assert.AreEqual((ulong)5000, envelope.Payload.UptimeMs);
    }

    [TestMethod]
    public void HealthSnapshotSerializesProxyRttMsWhenNull()
    {
        var payload = new CoreHealthSnapshotPayload(
            CoreState: "running",
            ProxyState: "connected",
            UptimeMs: 5000,
            TcpConnectTotal: 10,
            TcpActive: 1,
            TcpClosedTotal: 9,
            UdpEventTotal: 5,
            DnsQueryTotal: 2,
            DnsFailureTotal: 0,
            RedirectSuccessTotal: 10,
            RedirectFailureTotal: 0,
            RxBytes: 1024,
            TxBytes: 512,
            NetworkErrorTotal: 0,
            V2RayRunning: true,
            LocalSocksRunning: true,
            ShadowsocksConnected: true,
            DroppedTelemetryEvents: 0,
            ProxyRttMs: null);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("proxy_rtt_ms", out var rttProp));
        Assert.AreEqual(JsonValueKind.Null, rttProp.ValueKind);

        var deserialized = JsonSerializer.Deserialize<CoreHealthSnapshotPayload>(json);
        Assert.IsNotNull(deserialized);
        Assert.IsNull(deserialized.ProxyRttMs);
        Assert.AreEqual("running", deserialized.CoreState);
        Assert.AreEqual((ulong)1024, deserialized.RxBytes);
        Assert.AreEqual((ulong)512, deserialized.TxBytes);
        Assert.AreEqual((ulong)5000, deserialized.UptimeMs);
    }

    [TestMethod]
    public void HealthSnapshotSerializesProxyRttMsWhenZero()
    {
        var payload = new CoreHealthSnapshotPayload(
            CoreState: "running",
            ProxyState: "connected",
            UptimeMs: 5000,
            TcpConnectTotal: 10,
            TcpActive: 1,
            TcpClosedTotal: 9,
            UdpEventTotal: 5,
            DnsQueryTotal: 2,
            DnsFailureTotal: 0,
            RedirectSuccessTotal: 10,
            RedirectFailureTotal: 0,
            RxBytes: 1024,
            TxBytes: 512,
            NetworkErrorTotal: 0,
            V2RayRunning: true,
            LocalSocksRunning: true,
            ShadowsocksConnected: true,
            DroppedTelemetryEvents: 0,
            ProxyRttMs: 0);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("proxy_rtt_ms", out var rttProp));
        Assert.AreEqual(JsonValueKind.Number, rttProp.ValueKind);
        Assert.AreEqual(0, rttProp.GetInt32());

        var deserialized = JsonSerializer.Deserialize<CoreHealthSnapshotPayload>(json);
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(0, deserialized.ProxyRttMs);
    }

    [TestMethod]
    public void HealthSnapshotSerializesProxyRttMsWhenPositive()
    {
        var payload = new CoreHealthSnapshotPayload(
            CoreState: "running",
            ProxyState: "connected",
            UptimeMs: 5000,
            TcpConnectTotal: 10,
            TcpActive: 1,
            TcpClosedTotal: 9,
            UdpEventTotal: 5,
            DnsQueryTotal: 2,
            DnsFailureTotal: 0,
            RedirectSuccessTotal: 10,
            RedirectFailureTotal: 0,
            RxBytes: 1024,
            TxBytes: 512,
            NetworkErrorTotal: 0,
            V2RayRunning: true,
            LocalSocksRunning: true,
            ShadowsocksConnected: true,
            DroppedTelemetryEvents: 0,
            ProxyRttMs: 42);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("proxy_rtt_ms", out var rttProp));
        Assert.AreEqual(JsonValueKind.Number, rttProp.ValueKind);
        Assert.AreEqual(42, rttProp.GetInt32());

        var deserialized = JsonSerializer.Deserialize<CoreHealthSnapshotPayload>(json);
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(42, deserialized.ProxyRttMs);
    }

    [TestMethod]
    public void HealthSnapshotDeserializationHandlesOmittedProxyRttMs()
    {
        const string legacyJson = "{\"core_state\":\"running\",\"proxy_state\":\"connected\",\"uptime_ms\":5000,\"tcp_connect_total\":10,\"tcp_active\":1,\"tcp_closed_total\":9,\"udp_event_total\":5,\"dns_query_total\":2,\"dns_failure_total\":0,\"redirect_success_total\":10,\"redirect_failure_total\":0,\"rx_bytes\":1024,\"tx_bytes\":512,\"network_error_total\":0,\"v2ray_running\":true,\"local_socks_running\":true,\"shadowsocks_connected\":true,\"dropped_telemetry_events\":0}";

        var deserialized = JsonSerializer.Deserialize<CoreHealthSnapshotPayload>(legacyJson);
        Assert.IsNotNull(deserialized);
        Assert.IsNull(deserialized.ProxyRttMs);
        Assert.AreEqual("running", deserialized.CoreState);
        Assert.AreEqual("connected", deserialized.ProxyState);
        Assert.AreEqual((ulong)5000, deserialized.UptimeMs);
        Assert.AreEqual((ulong)1024, deserialized.RxBytes);
        Assert.AreEqual((ulong)512, deserialized.TxBytes);
        Assert.IsTrue(deserialized.V2RayRunning);
        Assert.IsTrue(deserialized.LocalSocksRunning);
        Assert.IsTrue(deserialized.ShadowsocksConnected);
    }

    [TestMethod]
    public void HealthSnapshotPayloadContainsNoProxyHostOrEndpointIdentity()
    {
        var payload = new CoreHealthSnapshotPayload(
            CoreState: "running",
            ProxyState: "connected",
            UptimeMs: 125000,
            TcpConnectTotal: 412,
            TcpActive: 8,
            TcpClosedTotal: 404,
            UdpEventTotal: 95,
            DnsQueryTotal: 64,
            DnsFailureTotal: 0,
            RedirectSuccessTotal: 412,
            RedirectFailureTotal: 0,
            RxBytes: 154820912,
            TxBytes: 12490184,
            NetworkErrorTotal: 0,
            V2RayRunning: true,
            LocalSocksRunning: true,
            ShadowsocksConnected: true,
            DroppedTelemetryEvents: 0,
            ProxyRttMs: 42);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var propertyNames = new System.Collections.Generic.HashSet<string>();
        foreach (var prop in root.EnumerateObject())
        {
            propertyNames.Add(prop.Name);
        }

        string[] forbiddenSubstrings = { "host", "ip", "port", "address", "endpoint", "server", "target", "user", "pass", "secret", "token", "jwt", "permit", "key" };
        foreach (var propName in propertyNames)
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.IsFalse(
                    propName.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains(forbidden + "_", StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains("_" + forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Payload property '{propName}' violates privacy contract by containing '{forbidden}'.");
            }
        }

        var expectedProperties = new System.Collections.Generic.HashSet<string>
        {
            "core_state",
            "proxy_state",
            "uptime_ms",
            "tcp_connect_total",
            "tcp_active",
            "tcp_closed_total",
            "udp_event_total",
            "dns_query_total",
            "dns_failure_total",
            "redirect_success_total",
            "redirect_failure_total",
            "rx_bytes",
            "tx_bytes",
            "network_error_total",
            "v2ray_running",
            "local_socks_running",
            "shadowsocks_connected",
            "dropped_telemetry_events",
            "proxy_rtt_ms"
        };

        Assert.IsTrue(expectedProperties.SetEquals(propertyNames));
    }

    [TestMethod]
    public void ComponentErrorPayloadIsSanitized()
    {
        var payload = new ComponentErrorPayload(
            Component: "proxy",
            ErrorCode: "StartFailed",
            Severity: "error",
            Recoverable: false);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("proxy", root.GetProperty("component").GetString());
        Assert.AreEqual("StartFailed", root.GetProperty("error_code").GetString());
        Assert.AreEqual("error", root.GetProperty("severity").GetString());
        Assert.IsFalse(root.GetProperty("recoverable").GetBoolean());

        // Verify no arbitrary message, exception or secrets
        Assert.IsFalse(root.TryGetProperty("message", out _));
        Assert.IsFalse(root.TryGetProperty("stack_trace", out _));
        Assert.IsFalse(root.TryGetProperty("jwt", out _));
        Assert.IsFalse(root.TryGetProperty("permit", out _));
        Assert.IsFalse(root.TryGetProperty("password", out _));
    }
}
