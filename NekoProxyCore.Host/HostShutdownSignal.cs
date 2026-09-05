namespace NekoProxyCore.Host;

internal sealed class HostShutdownSignal : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    public CancellationToken Token => _cancellation.Token;

    public bool IsShutdownRequested => _cancellation.IsCancellationRequested;

    public void RequestShutdown() => _cancellation.Cancel();

    public void Dispose() => _cancellation.Dispose();
}