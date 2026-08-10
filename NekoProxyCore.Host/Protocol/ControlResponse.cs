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
        FromResult(result, correlationId, NullCoreDiagnosticSink.Instance);

    internal static ControlResponse FromResult(
        ProxyResult result,
        string? correlationId,
        ICoreDiagnosticSink diagnostics) =>
        new(
            "result",
            correlationId ?? result.CorrelationId,
            result.Status,
            result.Succeeded,
            MapWireError(result.Error?.Code, diagnostics));

    public static ControlResponse FromStatus(ProxyStatusSnapshot status, string correlationId) =>
        FromStatus(status, correlationId, NullCoreDiagnosticSink.Instance);

    internal static ControlResponse FromStatus(
        ProxyStatusSnapshot status,
        string correlationId,
        ICoreDiagnosticSink diagnostics) =>
        new(
            "status",
            correlationId,
            status.Status,
            status.Status != ProxyStatusKind.Failed,
            status.Status == ProxyStatusKind.Failed
                ? MapFailedStatusError(status.Error?.Code, diagnostics)
                : null);

    public static ControlResponse ShutdownSuccess(string correlationId) =>
        new("shutdownResponse", correlationId, ProxyStatusKind.Stopped, true, null);

    public static ControlResponse ShutdownFailure(ProxyResult result, string correlationId) =>
        ShutdownFailure(result, correlationId, NullCoreDiagnosticSink.Instance);

    internal static ControlResponse ShutdownFailure(
        ProxyResult result,
        string correlationId,
        ICoreDiagnosticSink diagnostics) =>
        new(
            "shutdownResponse",
            correlationId,
            result.Status,
            false,
            MapWireError(result.Error?.Code, diagnostics));

    private static ProxyErrorCode? MapWireError(
        ProxyErrorCode? code,
        ICoreDiagnosticSink? diagnostics) => code switch
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
        _ => TranslateUnavailable(diagnostics)
    };

    private static ProxyErrorCode TranslateUnavailable(ICoreDiagnosticSink? diagnostics)
    {
        CoreDiagnosticReporter.ReportSafely(
            diagnostics ?? NullCoreDiagnosticSink.Instance,
            CoreDiagnosticStage.ControlResponse,
            CoreDiagnosticCategory.ControlErrorTranslatedToAuthorizationUnavailable);
        return ProxyErrorCode.AuthorizationUnavailable;
    }

    private static ProxyErrorCode MapFailedStatusError(
        ProxyErrorCode? code,
        ICoreDiagnosticSink? diagnostics)
    {
        var mapped = MapWireError(code, diagnostics);
        if (mapped is { } errorCode)
            return errorCode;

        CoreDiagnosticReporter.ReportSafely(
            diagnostics ?? NullCoreDiagnosticSink.Instance,
            CoreDiagnosticStage.ControlResponse,
            CoreDiagnosticCategory.ControlFailedStatusErrorMissing);
        return ProxyErrorCode.AuthorizationUnavailable;
    }

    internal static ControlResponse ProtocolInvalid(string correlationId = "invalid") =>
        new("result", correlationId, ProxyStatusKind.Failed, false, ProxyErrorCode.ProtocolInvalid);
}
