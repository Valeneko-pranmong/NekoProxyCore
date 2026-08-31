using System.Text.Json.Serialization;

namespace NekoProxyCore.Core;

public sealed record CoreHealthSnapshotPayload(
    [property: JsonPropertyName("core_state")] string CoreState,
    [property: JsonPropertyName("proxy_state")] string ProxyState,
    [property: JsonPropertyName("uptime_ms")] ulong UptimeMs,
    [property: JsonPropertyName("tcp_connect_total")] ulong TcpConnectTotal,
    [property: JsonPropertyName("tcp_active")] uint TcpActive,
    [property: JsonPropertyName("tcp_closed_total")] ulong TcpClosedTotal,
    [property: JsonPropertyName("udp_event_total")] ulong UdpEventTotal,
    [property: JsonPropertyName("dns_query_total")] ulong DnsQueryTotal,
    [property: JsonPropertyName("dns_failure_total")] ulong DnsFailureTotal,
    [property: JsonPropertyName("redirect_success_total")] ulong RedirectSuccessTotal,
    [property: JsonPropertyName("redirect_failure_total")] ulong RedirectFailureTotal,
    [property: JsonPropertyName("rx_bytes")] ulong RxBytes,
    [property: JsonPropertyName("tx_bytes")] ulong TxBytes,
    [property: JsonPropertyName("network_error_total")] ulong NetworkErrorTotal,
    [property: JsonPropertyName("v2ray_running")] bool V2RayRunning,
    [property: JsonPropertyName("local_socks_running")] bool LocalSocksRunning,
    [property: JsonPropertyName("shadowsocks_connected")] bool ShadowsocksConnected,
    [property: JsonPropertyName("dropped_telemetry_events")] ulong DroppedTelemetryEvents,
    [property: JsonPropertyName("proxy_rtt_ms")] int? ProxyRttMs);
