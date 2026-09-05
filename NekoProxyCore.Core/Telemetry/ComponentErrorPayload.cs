using System.Text.Json.Serialization;

namespace NekoProxyCore.Core;

public sealed record ComponentErrorPayload(
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("error_code")] string ErrorCode,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("recoverable")] bool Recoverable);
