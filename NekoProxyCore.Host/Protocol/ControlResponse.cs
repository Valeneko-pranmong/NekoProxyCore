using NekoProxyCore.Core;

namespace NekoProxyCore.Host.Protocol;

public sealed class ControlResponse
{
    internal ControlResponse(
        string kind,
        string correlationId,
        ProxyStatusKind status,
        bool succeeded,
        ProxyErrorCode? errorCode)
    {
        Kind = kind;
        CorrelationId = correlationId;
        Status = status;
        Succeeded = succeeded;
        ErrorCode = errorCode;
    }

    public string Kind { get; }

    public string CorrelationId { get; }

    public ProxyStatusKind Status { get; }

    public bool Succeeded { get; }

    public ProxyErrorCode? ErrorCode { get; }

    public static ControlResponse FromResult(ProxyResult result, string? correlationId = null) =>
        new(
            "result",
            correlationId ?? result.CorrelationId,
            result.Status,
            result.Succeeded,
            MapWireError(result.Error?.Code));

    public static ControlResponse FromStatus(ProxyStatusSnapshot status, string correlationId) =>
        new(
            "status",
            correlationId,
            status.Status,
            status.Status != ProxyStatusKind.Failed,
            status.Status == ProxyStatusKind.Failed
                ? MapWireError(status.Error?.Code) ?? ProxyErrorCode.AuthorizationUnavailable
                : null);

    public static ControlResponse ShutdownSuccess(string correlationId) =>
        new("shutdownResponse", correlationId, ProxyStatusKind.Stopped, true, null);

    public static ControlResponse ShutdownFailure(ProxyResult result, string correlationId) =>
        new("shutdownResponse", correlationId, result.Status, false, MapWireError(result.Error?.Code));

    private static ProxyErrorCode? MapWireError(ProxyErrorCode? code) => code switch
    {
        null => null,
        ProxyErrorCode.AuthorizationRequired or
        ProxyErrorCode.AuthorizationInvalid or
        ProxyErrorCode.AuthorizationExpired or
        ProxyErrorCode.AuthorizationReplay or
        ProxyErrorCode.AuthorizationUnavailable or
        ProxyErrorCode.SessionInactive or
        ProxyErrorCode.EntitlementInactive or
        ProxyErrorCode.HeartbeatStale or
        ProxyErrorCode.ProcessNotFound or
        ProxyErrorCode.ProcessExited or
        ProxyErrorCode.ConfigurationMismatch or
        ProxyErrorCode.ProtocolInvalid or
        ProxyErrorCode.AlreadyRunning or
        ProxyErrorCode.StartTimeout or
        ProxyErrorCode.Cancelled or
        ProxyErrorCode.StartFailed or
        ProxyErrorCode.StopFailed => code,
        _ => ProxyErrorCode.AuthorizationUnavailable
    };

    internal static ControlResponse ProtocolInvalid(string correlationId = "invalid") =>
        new("result", correlationId, ProxyStatusKind.Failed, false, ProxyErrorCode.ProtocolInvalid);
}
