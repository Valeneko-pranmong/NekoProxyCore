using System.Diagnostics;

namespace NekoProxyCore.Core;

public interface ICoreHealthProvider
{
    Task<CoreHealthSnapshotPayload> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class CoreTelemetryAggregator : ICoreHealthProvider
{
    private readonly IProxyRuntime _runtime;
    private readonly ITelemetryPublisher _publisher;
    private readonly TimeSpan _interval;
    private readonly Stopwatch _uptimeStopwatch;

    public CoreTelemetryAggregator(
        IProxyRuntime runtime,
        ITelemetryPublisher publisher,
        TimeSpan? interval = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _interval = interval ?? TimeSpan.FromMilliseconds(1000);
        _uptimeStopwatch = Stopwatch.StartNew();
    }

    public async Task<CoreHealthSnapshotPayload> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var statusSnapshot = await _runtime.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var proxyState = MapProxyState(statusSnapshot.Status);
        var isConnected = statusSnapshot.Status == ProxyStatusKind.Running;

        return new CoreHealthSnapshotPayload(
            CoreState: "running",
            ProxyState: proxyState,
            UptimeMs: (ulong)_uptimeStopwatch.ElapsedMilliseconds,
            V2RayRunning: isConnected,
            LocalSocksRunning: isConnected,
            ShadowsocksConnected: isConnected,
            DroppedTelemetryEvents: _publisher.DroppedEventsCount);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var snapshot = await GetCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    _publisher.Publish("core.health.snapshot", "core", snapshot);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Fail-safe: aggregation exceptions must never crash the host
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
    }

    public async Task EmitSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await GetCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
            _publisher.Publish("core.health.snapshot", "core", snapshot);
        }
        catch
        {
            // Fail-safe
        }
    }

    private static string MapProxyState(ProxyStatusKind status) => status switch
    {
        ProxyStatusKind.Stopped => "stopped",
        ProxyStatusKind.Starting => "connecting",
        ProxyStatusKind.Running => "connected",
        ProxyStatusKind.Stopping => "stopping",
        ProxyStatusKind.Failed => "degraded",
        _ => "unknown"
    };
}
