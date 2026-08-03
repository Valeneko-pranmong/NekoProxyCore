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

    public static ControlResponse FromResult(ProxyResult result) =>
        new("result", result.CorrelationId, result.Status, result.Succeeded, result.Error?.Code);

    public static ControlResponse FromStatus(ProxyStatusSnapshot status, string correlationId) =>
        new("status", correlationId, status.Status, status.Status != ProxyStatusKind.Failed, status.Error?.Code);

    internal static ControlResponse InvalidConfiguration(string correlationId = "invalid") =>
        new("result", correlationId, ProxyStatusKind.Failed, false, ProxyErrorCode.InvalidConfiguration);
}
