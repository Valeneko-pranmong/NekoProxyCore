namespace NekoProxyCore.Core;

/// <summary>
/// ProcessMode seam. Native redirector/driver behavior is supplied by the engine adapter.
/// </summary>
public sealed class ProcessModeController : IProxyModeController, IRuntimeConfiguredProxyModeController, IProcessExitWatcher, IAuthorizedStartPrecondition
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

    public Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken) => StartCoreAsync(configuration, null, cancellationToken);

    public Task StartAsync(ProxyConfiguration configuration, RuntimeProxyConfig runtimeConfig, CancellationToken cancellationToken) =>
        StartCoreAsync(configuration, runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig)), cancellationToken);

    private async Task StartCoreAsync(ProxyConfiguration configuration, RuntimeProxyConfig? runtimeConfig, CancellationToken cancellationToken)
    {
        try
        {
            await VerifyAsync(configuration, cancellationToken).ConfigureAwait(false);
            Report(
                CoreDiagnosticStage.RuntimeTargetRecheck,
                CoreDiagnosticCategory.StageCompleted);
        }
        catch (OperationCanceledException)
        {
            Report(
                CoreDiagnosticStage.RuntimeTargetRecheck,
                CoreDiagnosticCategory.RuntimeTargetCancelled);
            throw;
        }
        catch (ProxyRuntimeException exception)
        {
            ReportRuntimeTargetFailure(CoreDiagnosticStage.RuntimeTargetRecheck, exception.Code);
            throw;
        }
        catch (Exception)
        {
            Report(
                CoreDiagnosticStage.RuntimeTargetRecheck,
                CoreDiagnosticCategory.RuntimeTargetUnexpectedException);
            throw;
        }

        if (runtimeConfig == null)
            await _engine.StartAsync(configuration, cancellationToken).ConfigureAwait(false);
        else if (_engine is IRuntimeConfiguredProcessModeEngine runtimeEngine)
            await runtimeEngine.StartAsync(configuration, runtimeConfig, cancellationToken).ConfigureAwait(false);
        else
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "Proxy configuration is invalid.");

        try
        {
            if (!await IsTargetRunningAsync(configuration, cancellationToken).ConfigureAwait(false))
            {
                Report(
                    CoreDiagnosticStage.RuntimeTargetPostcheck,
                    CoreDiagnosticCategory.RuntimeTargetProcessExited);
                throw new ProxyRuntimeException(
                    ProxyErrorCode.ProcessExited,
                    "The target process exited during startup.");
            }

            Report(
                CoreDiagnosticStage.RuntimeTargetPostcheck,
                CoreDiagnosticCategory.StageCompleted);
        }
        catch (OperationCanceledException)
        {
            Report(
                CoreDiagnosticStage.RuntimeTargetPostcheck,
                CoreDiagnosticCategory.RuntimeTargetCancelled);
            throw;
        }
        catch (ProxyRuntimeException exception)
        {
            if (exception.Code != ProxyErrorCode.ProcessExited)
                ReportRuntimeTargetFailure(CoreDiagnosticStage.RuntimeTargetPostcheck, exception.Code);
            throw;
        }
        catch (Exception)
        {
            Report(
                CoreDiagnosticStage.RuntimeTargetPostcheck,
                CoreDiagnosticCategory.RuntimeTargetUnexpectedException);
            throw;
        }
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

    private void ReportRuntimeTargetFailure(CoreDiagnosticStage stage, ProxyErrorCode code)
    {
        var category = code switch
        {
            ProxyErrorCode.ProcessNotFound => CoreDiagnosticCategory.RuntimeTargetProcessNotFound,
            ProxyErrorCode.ProcessExited => CoreDiagnosticCategory.RuntimeTargetProcessExited,
            ProxyErrorCode.AuthorizationUnavailable =>
                CoreDiagnosticCategory.RuntimeTargetVerificationUnavailable,
            ProxyErrorCode.UnsupportedMode => CoreDiagnosticCategory.RuntimeTargetUnsupportedMode,
            _ => CoreDiagnosticCategory.RuntimeTargetUnexpectedException
        };
        Report(stage, category);
    }

    private void Report(CoreDiagnosticStage stage, CoreDiagnosticCategory category) =>
        CoreDiagnosticReporter.ReportSafely(_diagnostics, stage, category);
}
