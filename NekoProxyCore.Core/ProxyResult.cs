namespace NekoProxyCore.Core;

public sealed class ProxyResult
{
    private ProxyResult(bool succeeded, ProxyStatusKind status, string correlationId, ProxyError? error)
    {
        Succeeded = succeeded;
        Status = status;
        CorrelationId = correlationId;
        Error = error;
    }

    public bool Succeeded { get; }

    public ProxyStatusKind Status { get; }

    public string CorrelationId { get; }

    public ProxyError? Error { get; }

    public static ProxyResult Success(ProxyStatusKind status, string correlationId) => new(true, status, correlationId, null);

    public static ProxyResult Failure(ProxyStatusKind status, string correlationId, ProxyError error) => new(false, status, correlationId, error);
}

public sealed class ProxyStatusSnapshot
{
    public ProxyStatusSnapshot(ProxyStatusKind status, string correlationId, DateTimeOffset timestamp, ProxyError? error = null)
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
