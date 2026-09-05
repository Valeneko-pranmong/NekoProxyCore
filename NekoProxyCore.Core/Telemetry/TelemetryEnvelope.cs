using System.Text.Json.Serialization;

namespace NekoProxyCore.Core;

public sealed record TelemetryEnvelope<T>(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("sequence")] ulong Sequence,
    [property: JsonPropertyName("timestamp_utc")] string TimestampUtc,
    [property: JsonPropertyName("message_type")] string MessageType,
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("payload")] T Payload);

public sealed record TelemetryEnvelope(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("sequence")] ulong Sequence,
    [property: JsonPropertyName("timestamp_utc")] string TimestampUtc,
    [property: JsonPropertyName("message_type")] string MessageType,
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("payload")] object Payload);
