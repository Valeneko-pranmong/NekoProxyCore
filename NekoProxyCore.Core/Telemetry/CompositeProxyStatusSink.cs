namespace NekoProxyCore.Core;

public sealed class CompositeProxyStatusSink : IProxyStatusSink
{
    private readonly IProxyStatusSink _primarySink;
    private readonly ITelemetryPublisher _telemetryPublisher;

    public CompositeProxyStatusSink(
        IProxyStatusSink primarySink,
        ITelemetryPublisher telemetryPublisher)
    {
        _primarySink = primarySink ?? throw new ArgumentNullException(nameof(primarySink));
        _telemetryPublisher = telemetryPublisher ?? throw new ArgumentNullException(nameof(telemetryPublisher));
    }

    public void OnStatusChanged(ProxyStatusEvent statusEvent)
    {
        try
        {
            _primarySink.OnStatusChanged(statusEvent);
        }
        finally
        {
            try
            {
                var messageType = MapStatusToMessageType(statusEvent.Status);
                if (statusEvent.Error != null)
                {
                    _telemetryPublisher.Publish(
                        messageType,
                        "proxy",
                        new ComponentErrorPayload(
                            Component: "proxy",
                            ErrorCode: statusEvent.Error.Code.ToString(),
                            Severity: "error",
                            Recoverable: false));
                }
                else
                {
                    _telemetryPublisher.PublishLifecycle(messageType, "proxy");
                }
            }
            catch
            {
                // Telemetry failure must never affect the primary status sink or runtime.
            }
        }
    }

    private static string MapStatusToMessageType(ProxyStatusKind status) => status switch
    {
        ProxyStatusKind.Starting => "proxy.starting",
        ProxyStatusKind.Running => "proxy.running",
        ProxyStatusKind.Stopping => "proxy.stopping",
        ProxyStatusKind.Stopped => "proxy.stopped",
        ProxyStatusKind.Failed => "proxy.failed",
        _ => "proxy.status"
    };
}
