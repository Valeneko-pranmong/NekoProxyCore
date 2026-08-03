using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NekoProxyCore.Core;

namespace NekoProxyCore.Host.Protocol;

public static class ControlProtocol
{
    public const int Version = 1;
    public const int MaxFrameBytes = 8 * 1024;

    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z0-9._-]{1,256}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex OpaqueReferencePattern = new(
        "^(profile|server)-[0-9]+$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool TryParseRequest(
        string frame,
        out ControlRequest? request,
        out ControlResponse? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrEmpty(frame) || Encoding.UTF8.GetByteCount(frame) > MaxFrameBytes)
            return Fail(out error);

        try
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetInt(root, "version", out var version) || version != Version ||
                !TryGetString(root, "command", out var commandText) ||
                !TryGetString(root, "correlationId", out var correlationId) ||
                !IsIdentifier(correlationId) ||
                !TryParseCommand(commandText, out var command))
                return Fail(out error);

            string? processName = null;
            string? profileReference = null;
            string? serverReference = null;
            if (command == ControlCommand.Start)
            {
                if (!TryGetString(root, "processName", out processName) ||
                    !string.Equals(processName, "pso2.exe", StringComparison.OrdinalIgnoreCase) ||
                    !TryGetString(root, "profileReference", out profileReference) ||
                    !OpaqueReferencePattern.IsMatch(profileReference) ||
                    !profileReference.StartsWith("profile-", StringComparison.Ordinal) ||
                    !TryGetString(root, "serverReference", out serverReference) ||
                    !OpaqueReferencePattern.IsMatch(serverReference) ||
                    !serverReference.StartsWith("server-", StringComparison.Ordinal))
                    return Fail(out error, correlationId);
            }

            request = new ControlRequest(command, correlationId, processName, profileReference, serverReference);
            return true;
        }
        catch (JsonException)
        {
            return Fail(out error);
        }
    }

    public static string Serialize(ControlResponse response) =>
        JsonSerializer.Serialize(new WireResponse(
            Version,
            response.Kind,
            response.CorrelationId,
            response.Status,
            response.Succeeded,
            response.ErrorCode), SerializerOptions);

    private static bool TryParseCommand(string value, out ControlCommand command)
    {
        switch (value.ToLowerInvariant())
        {
            case "start":
                command = ControlCommand.Start;
                return true;
            case "status":
                command = ControlCommand.Status;
                return true;
            case "stop":
                command = ControlCommand.Stop;
                return true;
            default:
                command = default;
                return false;
        }
    }

    private static bool IsIdentifier(string value) => IdentifierPattern.IsMatch(value);

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               (value = property.GetString() ?? string.Empty).Length > 0;
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = default;
        return root.TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }

    private static bool Fail(out ControlResponse? error, string correlationId = "invalid")
    {
        error = ControlResponse.InvalidConfiguration(IsIdentifier(correlationId) ? correlationId : "invalid");
        return false;
    }

    private sealed record WireResponse(
        int Version,
        string Kind,
        string CorrelationId,
        ProxyStatusKind Status,
        bool Succeeded,
        ProxyErrorCode? ErrorCode);
}
