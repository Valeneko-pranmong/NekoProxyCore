namespace NekoProxyCore.Core;

public sealed class ProxyStatusEvent
{
    public ProxyStatusEvent(ProxyStatusKind status, string correlationId, DateTimeOffset timestamp, ProxyError? error = null)
    {
        Status = status;
        CorrelationId = correlationId;
        Timestamp = timestamp;
        Error = error;
    }

    public ProxyStatusKind Status { get; }

    public string CorrelationId { get; }

    public DateTimeOffset Timestamp { get; }

    public ProxyError? Error { get; }
}
