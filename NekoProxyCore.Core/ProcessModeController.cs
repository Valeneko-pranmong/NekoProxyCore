namespace NekoProxyCore.Core;

/// <summary>
/// ProcessMode seam. Native redirector/driver behavior is supplied by the engine adapter.
/// </summary>
public sealed class ProcessModeController : IProxyModeController
{
    private readonly IProcessResolver _processResolver;
    private readonly IProcessModeEngine _engine;

    public ProcessModeController(IProcessResolver processResolver, IProcessModeEngine engine)
    {
        _processResolver = processResolver ?? throw new ArgumentNullException(nameof(processResolver));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public async Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode != ProxyModeKind.Process)
            throw new ProxyRuntimeException(ProxyErrorCode.UnsupportedMode, "The requested proxy mode is not supported.");

        if (!await _processResolver.IsRunningAsync(configuration.ProcessName, cancellationToken).ConfigureAwait(false))
            throw new ProxyRuntimeException(ProxyErrorCode.ProcessNotFound, "The target process is not running.");

        await _engine.StartAsync(configuration, cancellationToken).ConfigureAwait(false);

        if (!await _processResolver.IsRunningAsync(configuration.ProcessName, cancellationToken).ConfigureAwait(false))
            throw new ProxyRuntimeException(ProxyErrorCode.ProcessExited, "The target process exited during startup.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => _engine.StopAsync(cancellationToken);

    public Task WaitForProcessExitAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode != ProxyModeKind.Process)
            throw new ProxyRuntimeException(ProxyErrorCode.UnsupportedMode, "The requested proxy mode is not supported.");

        return _processResolver.WaitForExitAsync(configuration.ProcessName, cancellationToken);
    }
}
