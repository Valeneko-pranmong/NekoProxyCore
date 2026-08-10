namespace NekoProxyCore.Core;

/// <summary>
/// ProcessMode seam. Native redirector/driver behavior is supplied by the engine adapter.
/// </summary>
public sealed class ProcessModeController : IProxyModeController, IProcessExitWatcher, IAuthorizedStartPrecondition
{
    private readonly IProcessResolver _processResolver;
    private readonly IProcessModeEngine _engine;
    private readonly ICoreDiagnosticSink _diagnostics;

    public ProcessModeController(IProcessResolver processResolver, IProcessModeEngine engine)
        : this(processResolver, engine, null)
    {
    }

    public ProcessModeController(
        IProcessResolver processResolver,
        IProcessModeEngine engine,
        ICoreDiagnosticSink? diagnostics)
    {
        _processResolver = processResolver ?? throw new ArgumentNullException(nameof(processResolver));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _diagnostics = diagnostics ?? NullCoreDiagnosticSink.Instance;
    }

    public async Task VerifyAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode != ProxyModeKind.Process)
            throw new ProxyRuntimeException(ProxyErrorCode.UnsupportedMode, "The requested proxy mode is not supported.");

        if (!await IsTargetRunningAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            var code = configuration.TargetPid is null
                ? ProxyErrorCode.ProcessNotFound
                : ProxyErrorCode.ProcessExited;
            throw new ProxyRuntimeException(code, "The target process is not running.");
        }
    }

    public async Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
    {
        await VerifyAsync(configuration, cancellationToken).ConfigureAwait(false);
        await _engine.StartAsync(configuration, cancellationToken).ConfigureAwait(false);

        if (!await IsTargetRunningAsync(configuration, cancellationToken).ConfigureAwait(false))
            throw new ProxyRuntimeException(ProxyErrorCode.ProcessExited, "The target process exited during startup.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => _engine.StopAsync(cancellationToken);

    public Task WaitForProcessExitAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode != ProxyModeKind.Process)
            throw new ProxyRuntimeException(ProxyErrorCode.UnsupportedMode, "The requested proxy mode is not supported.");

        if (configuration.TargetPid is not { } targetPid)
            return _processResolver.WaitForExitAsync(configuration.ProcessName, cancellationToken);
        if (_processResolver is not IExactProcessResolver exactResolver)
        {
            ReportExactResolverUnavailable();
            throw new ProxyRuntimeException(
                ProxyErrorCode.AuthorizationUnavailable,
                "Exact target verification is unavailable.");
        }

        return exactResolver.WaitForExactProcessExitAsync(
            configuration.ProcessName,
            targetPid,
            cancellationToken);
    }

    private Task<bool> IsTargetRunningAsync(
        ProxyConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.TargetPid is not { } targetPid)
            return _processResolver.IsRunningAsync(configuration.ProcessName, cancellationToken);
        if (_processResolver is not IExactProcessResolver exactResolver)
        {
            ReportExactResolverUnavailable();
            throw new ProxyRuntimeException(
                ProxyErrorCode.AuthorizationUnavailable,
                "Exact target verification is unavailable.");
        }

        return exactResolver.IsExactProcessRunningAsync(
            configuration.ProcessName,
            targetPid,
            cancellationToken);
    }

    private void ReportExactResolverUnavailable() =>
        CoreDiagnosticReporter.ReportSafely(
            _diagnostics,
            CoreDiagnosticStage.ProcessPrecondition,
            CoreDiagnosticCategory.ProcessExactResolverUnavailable);
}
