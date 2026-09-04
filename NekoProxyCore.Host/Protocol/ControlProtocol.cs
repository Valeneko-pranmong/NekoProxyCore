using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NekoProxyCore.Core;

namespace NekoProxyCore.Host.Protocol;

public static class ControlProtocol
{
    public const string PipeName = "NekoProxyCoreControl";
    public const int Version = 3;
    public const int MaxFrameBytes = 8 * 1024;
    public const int MaxPermitCharacters = 4096;

    private static readonly Regex ExistingCorrelationIdPattern = new(
        "^[0-9a-f]{32}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex AbsoluteCorrelationIdPattern = new(
        "^[0-9a-f]{32}\\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex ProfileReferencePattern = new(
        "^profile-[0-9]{1,6}\\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex ServerReferencePattern = new(
        "^server-[0-9]{1,6}\\z",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool TryParseRequest(
        string frame,
        ICoreChallengeService? challengeService,
        out ControlRequest? request,
        out ControlResponse? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrEmpty(frame) || Encoding.UTF8.GetByteCount(frame) > MaxFrameBytes)
            return Fail(out error);

        try
        {
            using var document = JsonDocument.Parse(frame, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
                !TryGetString(root, "type", out var commandText) ||
                !TryGetString(root, "correlationId", out var correlationId) ||
                !TryParseCommand(commandText, out var command) ||
                !IsCorrelationIdValid(command, correlationId))
            {
                return Fail(out error);
            }

            var expectedFields = command switch
            {
                ControlCommand.Start => new HashSet<string>(StringComparer.Ordinal)
                {
                    "type", "correlationId", "protocolVersion", "processName", "targetPid", "mode",
                    "profileReference", "serverReference", "permit", "runtimeConfig"
                },
                ControlCommand.RuntimeConfigValidate => new HashSet<string>(StringComparer.Ordinal)
                {
                    "type", "correlationId", "profileReference", "serverReference"
                },
                _ => new HashSet<string>(StringComparer.Ordinal) { "type", "correlationId" }
            };
            if (!HasExactFields(root, expectedFields))
                return Fail(out error, correlationId);

            string? processName = null;
            uint? targetPid = null;
            string? profileReference = null;
            string? serverReference = null;
            SensitivePermit? permit = null;
            string? admittedChallenge = null;
            RuntimeProxyConfig? runtimeConfig = null;
            if (command == ControlCommand.Start)
            {
                if (!TryGetInt(root, "protocolVersion", out var protocolVersion) || protocolVersion != Version ||
                    !TryGetString(root, "processName", out processName) ||
                    !string.Equals(processName, "pso2.exe", StringComparison.Ordinal) ||
                    !TryGetUInt(root, "targetPid", out var parsedTargetPid) || parsedTargetPid == 0 ||
                    !TryGetString(root, "mode", out var mode) ||
                    !string.Equals(mode, "ProcessMode", StringComparison.Ordinal) ||
                    !TryGetString(root, "profileReference", out profileReference) ||
                    !ProfileReferencePattern.IsMatch(profileReference) ||
                    !TryGetString(root, "serverReference", out serverReference) ||
                    !ServerReferencePattern.IsMatch(serverReference) ||
                    !TryGetString(root, "permit", out var permitText) ||
                    !IsStructurallyBoundedCompactPermit(permitText) ||
                    !SensitivePermit.TryCreate(permitText, MaxPermitCharacters, out permit) ||
                    !TryParseRuntimeConfig(root, out runtimeConfig))
                {
                    return Fail(out error, correlationId);
                }

                targetPid = parsedTargetPid;
                if (challengeService is null)
                    return Fail(out error, correlationId);

                var challenge = challengeService.ConsumeOutstandingForAttempt();
                if (challenge.Consumption != ChallengeConsumption.Accepted)
                    return Fail(out error, correlationId);
                admittedChallenge = challenge.Value;
            }
            else if (command == ControlCommand.RuntimeConfigValidate)
            {
                if (!TryGetString(root, "profileReference", out profileReference) ||
                    !ProfileReferencePattern.IsMatch(profileReference) ||
                    !TryGetString(root, "serverReference", out serverReference) ||
                    !ServerReferencePattern.IsMatch(serverReference))
                {
                    return Fail(out error, correlationId);
                }
            }

            request = new ControlRequest(
                command,
                correlationId,
                processName,
                targetPid,
                profileReference,
                serverReference,
                permit,
                admittedChallenge,
                runtimeConfig);
            return true;
        }
        catch (JsonException)
        {
            return Fail(out error);
        }
        catch (ArgumentException)
        {
            return Fail(out error);
        }
    }

    public static bool TryParseRequest(
        string frame,
        out ControlRequest? request,
        out ControlResponse? error) =>
        TryParseRequest(frame, null, out request, out error);

    public static string Serialize(ControlResponse response, string? responseType = null) =>
        JsonSerializer.Serialize(new WireResponse(
            responseType ?? response.Kind,
            response.CorrelationId,
            response.Status,
            response.Succeeded,
            response.ErrorCode), SerializerOptions);

    public static string SerializeChallenge(string correlationId, CoreChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (!ExistingCorrelationIdPattern.IsMatch(correlationId) || challenge.Value.Length != 43)
            throw new ArgumentException("Challenge response is invalid.");

        return JsonSerializer.Serialize(new WireChallengeResponse(
            "challengeResponse",
            correlationId,
            challenge.Value), SerializerOptions);
    }

    public static string SerializeRuntimeConfigCatalog(
        string correlationId,
        ProcessModeConfigurationCatalogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!AbsoluteCorrelationIdPattern.IsMatch(correlationId))
            throw new ArgumentException("Catalog response correlation ID is invalid.", nameof(correlationId));

        if (!result.Succeeded)
        {
            if (result.FailureReason is null || result.Candidates.Count != 0)
                throw new ArgumentException("Catalog failure response is invalid.", nameof(result));

            return JsonSerializer.Serialize(new WireRuntimeConfigCatalogResponse(
                "runtimeConfigCatalogResponse",
                correlationId,
                false,
                null,
                result.FailureReason), SerializerOptions);
        }

        if (result.FailureReason is not null ||
            result.Candidates.Count > ProcessModeConfigurationCatalogContract.MaximumCandidates)
        {
            throw new ArgumentException("Catalog success response is invalid.", nameof(result));
        }

        var previousProfileIndex = -1;
        var previousServerIndex = -1;
        foreach (var candidate in result.Candidates)
        {
            if (!ProcessModeConfigurationReference.TryParseProfile(candidate.ProfileReference, out var profileIndex) ||
                !ProcessModeConfigurationReference.TryParseServer(candidate.ServerReference, out var serverIndex) ||
                !candidate.RelationshipValid ||
                candidate.ProcessModeMatchCount != 1 ||
                profileIndex < previousProfileIndex ||
                profileIndex == previousProfileIndex && serverIndex <= previousServerIndex)
            {
                throw new ArgumentException("Catalog candidate is invalid.", nameof(result));
            }

            previousProfileIndex = profileIndex;
            previousServerIndex = serverIndex;
        }

        return JsonSerializer.Serialize(new WireRuntimeConfigCatalogResponse(
            "runtimeConfigCatalogResponse",
            correlationId,
            true,
            result.Candidates,
            null), SerializerOptions);
    }

    public static string SerializeRuntimeConfigValidation(
        string correlationId,
        ProcessModeConfigurationValidation validation,
        bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (!AbsoluteCorrelationIdPattern.IsMatch(correlationId) ||
            !ProcessModeConfigurationReference.TryParseProfile(validation.ProfileReference, out _) ||
            !ProcessModeConfigurationReference.TryParseServer(validation.ServerReference, out _) ||
            validation.ProcessModeMatchCount is < 0 or > 2 ||
            validation.Valid !=
                (validation.RelationshipValid && validation.ProcessModeMatchCount == 1) ||
            !succeeded &&
                (validation.RelationshipValid ||
                 validation.ProcessModeMatchCount != 0 ||
                 validation.Valid))
        {
            throw new ArgumentException("Validation response is invalid.");
        }

        return JsonSerializer.Serialize(new WireRuntimeConfigValidationResponse(
            "runtimeConfigValidateResponse",
            correlationId,
            succeeded,
            validation.ProfileReference,
            validation.ServerReference,
            validation.RelationshipValid,
            validation.ProcessModeMatchCount,
            validation.Valid), SerializerOptions);
    }

    private static bool TryParseCommand(string value, out ControlCommand command)
    {
        switch (value)
        {
            case "challenge":
                command = ControlCommand.Challenge;
                return true;
            case "start":
                command = ControlCommand.Start;
                return true;
            case "status":
                command = ControlCommand.Status;
                return true;
            case "stop":
                command = ControlCommand.Stop;
                return true;
            case "shutdown":
                command = ControlCommand.Shutdown;
                return true;
            case "runtimeConfigCatalog":
                command = ControlCommand.RuntimeConfigCatalog;
                return true;
            case "runtimeConfigValidate":
                command = ControlCommand.RuntimeConfigValidate;
                return true;
            default:
                command = default;
                return false;
        }
    }

    private static bool IsCorrelationIdValid(ControlCommand command, string correlationId) =>
        command is ControlCommand.RuntimeConfigCatalog or ControlCommand.RuntimeConfigValidate
            ? AbsoluteCorrelationIdPattern.IsMatch(correlationId)
            : ExistingCorrelationIdPattern.IsMatch(correlationId);

    private static bool HasDuplicateProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
                return true;
        }

        return false;
    }

    private static bool HasExactFields(JsonElement root, HashSet<string> expectedFields)
    {
        var count = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (!expectedFields.Contains(property.Name))
                return false;
            count++;
        }

        return count == expectedFields.Count;
    }

    private static bool IsStructurallyBoundedCompactPermit(string value)
    {
        if (value.Length is < 1 or > MaxPermitCharacters)
            return false;

        var segments = value.Split('.');
        return segments.Length == 3 && segments.All(segment =>
            segment.Length > 0 &&
            segment.All(character =>
                character is >= 'A' and <= 'Z' or
                    >= 'a' and <= 'z' or
                    >= '0' and <= '9' or
                    '-' or '_'));
    }

    private static bool TryParseRuntimeConfig(JsonElement root, out RuntimeProxyConfig? config)
    {
        config = null;
        if (!root.TryGetProperty("runtimeConfig", out var value) ||
            value.ValueKind != JsonValueKind.Object || HasDuplicateProperties(value) ||
            !HasExactFields(value, new HashSet<string>(StringComparer.Ordinal)
            {
                "schemaVersion", "configVersion", "endpointId", "host", "port", "protocol",
                "cipher", "credential", "issuedAt", "expiresAt"
            }) ||
            !TryGetInt(value, "schemaVersion", out var schemaVersion) ||
            !TryGetLong(value, "configVersion", out var configVersion) ||
            !TryGetString(value, "endpointId", out var endpointId) ||
            !TryGetString(value, "host", out var host) ||
            !TryGetInt(value, "port", out var port) ||
            !TryGetString(value, "protocol", out var protocol) ||
            !TryGetString(value, "cipher", out var cipher) ||
            !TryGetString(value, "credential", out var credential) ||
            !TryGetLong(value, "issuedAt", out var issuedAt) ||
            !TryGetLong(value, "expiresAt", out var expiresAt))
        {
            return false;
        }

        config = new RuntimeProxyConfig(
            schemaVersion, configVersion, endpointId, host, port, protocol, cipher,
            new SensitiveRuntimeCredential(credential), issuedAt, expiresAt);
        return true;
    }

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
        return root.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryGetUInt(JsonElement root, string name, out uint value)
    {
        value = default;
        return root.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetUInt32(out value);
    }

    private static bool TryGetLong(JsonElement root, string name, out long value)
    {
        value = default;
        return root.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }

    private static bool Fail(out ControlResponse? error, string correlationId = "invalid")
    {
        error = ControlResponse.ProtocolInvalid(
            ExistingCorrelationIdPattern.IsMatch(correlationId) ? correlationId : "invalid");
        return false;
    }

    private sealed record WireResponse(
        string Type,
        string CorrelationId,
        ProxyStatusKind Status,
        bool Succeeded,
        ProxyErrorCode? ErrorCode);

    private sealed record WireChallengeResponse(
        string Type,
        string CorrelationId,
        string Challenge);

    private sealed record WireRuntimeConfigCatalogResponse(
        string Type,
        string CorrelationId,
        bool Succeeded,
        IReadOnlyList<ProcessModeConfigurationCandidate>? Candidates,
        ProcessModeConfigurationCatalogFailureReason? Reason);

    private sealed record WireRuntimeConfigValidationResponse(
        string Type,
        string CorrelationId,
        bool Succeeded,
        string ProfileReference,
        string ServerReference,
        bool RelationshipValid,
        int ProcessModeMatchCount,
        bool Valid);
}
