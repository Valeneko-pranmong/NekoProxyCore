namespace NekoProxyCore.Core;

public interface IProxyRuntime
{
    Task<ProxyResult> StartAsync(ProxyStartRequest request);

    Task<ProxyResult> StopAsync(CancellationToken cancellationToken = default);

    Task<ProxyStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);
}

public interface IProxyStatusSink
{
    void OnStatusChanged(ProxyStatusEvent statusEvent);
}

public interface IProcessResolver
{
    Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for the process to disappear using an event/handle-based implementation.
    /// Implementations must not use an unbounded polling watcher.
    /// </summary>
    Task WaitForExitAsync(string processName, CancellationToken cancellationToken);
}

public interface IProxyModeController
{
    Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IProcessModeEngine
{
    Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class ProxyRuntimeException : Exception
{
    public ProxyRuntimeException(ProxyErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ProxyErrorCode Code { get; }
}
