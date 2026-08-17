using System.Text.Json.Serialization;

namespace NekoProxyCore.Core;

public sealed record CoreHealthSnapshotPayload(
    [property: JsonPropertyName("core_state")] string CoreState,
    [property: JsonPropertyName("proxy_state")] string ProxyState,
    [property: JsonPropertyName("uptime_ms")] ulong UptimeMs,
    [property: JsonPropertyName("v2ray_running")] bool V2RayRunning,
    [property: JsonPropertyName("local_socks_running")] bool LocalSocksRunning,
    [property: JsonPropertyName("shadowsocks_connected")] bool ShadowsocksConnected,
    [property: JsonPropertyName("dropped_telemetry_events")] ulong DroppedTelemetryEvents);
