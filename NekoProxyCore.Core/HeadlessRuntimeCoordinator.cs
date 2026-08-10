namespace NekoProxyCore.Core;

public sealed class HeadlessRuntimeCoordinator : IProxyRuntime
{
    private readonly IProxyModeController _modeController;
    private readonly IProxyStartAuthorizer _startAuthorizer;
    private readonly IProxyStatusSink _statusSink;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ICoreDiagnosticSink _diagnostics;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private ProxyStatusSnapshot _snapshot;
    private ProxyConfiguration? _activeConfiguration;
    private Task? _pendingStartTask;
    private CancellationTokenSource? _processExitMonitorCancellation;

    public HeadlessRuntimeCoordinator(
        IProxyModeController modeController,
        IProxyStatusSink? statusSink = null,
        Func<DateTimeOffset>? clock = null)
        : this(
            modeController,
            new AuthorizationRequiredStartAuthorizer(),
            statusSink,
            clock,
            NullCoreDiagnosticSink.Instance)
    {
    }

    public HeadlessRuntimeCoordinator(
        IProxyModeController modeController,
        IProxyStartAuthorizer startAuthorizer,
        IProxyStatusSink? statusSink = null,
        Func<DateTimeOffset>? clock = null)
        : this(modeController, startAuthorizer, statusSink, clock, NullCoreDiagnosticSink.Instance)
    {
    }

    public HeadlessRuntimeCoordinator(
        IProxyModeController modeController,
        IProxyStartAuthorizer startAuthorizer,
        IProxyStatusSink? statusSink,
        Func<DateTimeOffset>? clock,
        ICoreDiagnosticSink? diagnostics)
    {
        _modeController = modeController ?? throw new ArgumentNullException(nameof(modeController));
        _startAuthorizer = startAuthorizer ?? throw new ArgumentNullException(nameof(startAuthorizer));
        _statusSink = statusSink ?? NullProxyStatusSink.Instance;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _diagnostics = diagnostics ?? NullCoreDiagnosticSink.Instance;
        _snapshot = new ProxyStatusSnapshot(ProxyStatusKind.Stopped, string.Empty, _clock());
    }

    public async Task<ProxyResult> StartAsync(ProxyStartRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            await _lifecycleGate.WaitAsync(request.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            return ProxyResult.Failure(
                _snapshot.Status,
                request.CorrelationId,
                new ProxyError(ProxyErrorCode.Cancelled, "Proxy start was cancelled."));
        }
        try
        {
            if (_activeConfiguration != null ||
                _snapshot.Status is ProxyStatusKind.Starting or ProxyStatusKind.Running or ProxyStatusKind.Stopping)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.AlreadyRunning, "Proxy runtime is already running.");
            }

            try
            {
                ProxyError? authorizationError;
                try
                {
                    authorizationError = await _startAuthorizer.AuthorizeAsync(request).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    Report(
                        CoreDiagnosticStage.Authorization,
                        CoreDiagnosticCategory.RuntimeAuthorizationUnavailable);
                    return Fail(
                        request.CorrelationId,
                        ProxyErrorCode.AuthorizationUnavailable,
                        "Online authorization is unavailable.");
                }

                if (authorizationError != null)
                    return Fail(request.CorrelationId, authorizationError.Code, authorizationError.SafeMessage);

                if (_modeController is IAuthorizedStartPrecondition precondition)
                    await precondition.VerifyAsync(request.Configuration, request.CancellationToken).ConfigureAwait(false);
                Report(CoreDiagnosticStage.ProcessPrecondition, CoreDiagnosticCategory.StageCompleted);

                Publish(ProxyStatusKind.Starting, request.CorrelationId);
                _activeConfiguration = request.Configuration;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
                timeout.CancelAfter(request.Configuration.StartTimeout);
                Task? startTask = null;
                try
                {
                    startTask = _modeController.StartAsync(request.Configuration, timeout.Token);
                    _pendingStartTask = startTask;
                    await startTask
                        .WaitAsync(request.Configuration.StartTimeout, request.CancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    if (startTask != null && !startTask.IsCompleted)
                    {
                        _ = CleanupWhenPendingStartCompletesAsync(
                            startTask,
                            request.Configuration,
                            request.CorrelationId);
                        throw;
                    }

                    _pendingStartTask = null;
                    try
                    {
                        using var cleanupTimeout = new CancellationTokenSource(request.Configuration.StopTimeout);
                        await _modeController.StopAsync(cleanupTimeout.Token)
                            .WaitAsync(request.Configuration.StopTimeout)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        return Fail(
                            request.CorrelationId,
                            ProxyErrorCode.StopFailed,
                            "Proxy cleanup failed.");
                    }

                    _activeConfiguration = null;
                    throw;
                }

                _pendingStartTask = null;
                _activeConfiguration = request.Configuration;
                Publish(ProxyStatusKind.Running, request.CorrelationId);
                StartProcessExitMonitoring(request.Configuration, request.CorrelationId);
                Report(CoreDiagnosticStage.RuntimeStart, CoreDiagnosticCategory.StageCompleted);
                return ProxyResult.Success(ProxyStatusKind.Running, request.CorrelationId);
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.Cancelled, "Proxy start was cancelled.");
            }
            catch (OperationCanceledException)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.StartTimeout, "Proxy start timed out.");
            }
            catch (TimeoutException)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.StartTimeout, "Proxy start timed out.");
            }
            catch (ProxyRuntimeException e)
            {
                return Fail(request.CorrelationId, e.Code, GetAllowListedRuntimeMessage(e.Code));
            }
            catch (Exception)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.StartFailed, "Proxy start failed.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<ProxyResult> StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ProxyResult.Failure(
                _snapshot.Status,
                _snapshot.CorrelationId,
                new ProxyError(ProxyErrorCode.Cancelled, "Proxy stop was cancelled."));
        }
        try
        {
            if (_snapshot.Status == ProxyStatusKind.Stopped)
                return ProxyResult.Success(ProxyStatusKind.Stopped, _snapshot.CorrelationId);

            var correlationId = _snapshot.CorrelationId;
            if (_pendingStartTask != null)
            {
                return Fail(
                    correlationId,
                    ProxyErrorCode.StopFailed,
                    "Proxy start cleanup is pending.");
            }

            if (_activeConfiguration == null)
            {
                CancelProcessExitMonitoring();
                Publish(ProxyStatusKind.Stopped, correlationId);
                return ProxyResult.Success(ProxyStatusKind.Stopped, correlationId);
            }

            CancelProcessExitMonitoring();
            Publish(ProxyStatusKind.Stopping, correlationId);
            try
            {
                var stopTimeout = _activeConfiguration?.StopTimeout ?? TimeSpan.FromSeconds(15);
                using var timeout = new CancellationTokenSource(stopTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                await _modeController.StopAsync(linked.Token).WaitAsync(stopTimeout, cancellationToken).ConfigureAwait(false);
                _activeConfiguration = null;
                Publish(ProxyStatusKind.Stopped, correlationId);
                return ProxyResult.Success(ProxyStatusKind.Stopped, correlationId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Fail(correlationId, ProxyErrorCode.Cancelled, "Proxy stop was cancelled.");
            }
            catch (OperationCanceledException)
            {
                return Fail(correlationId, ProxyErrorCode.Timeout, "Proxy stop timed out.");
            }
            catch (TimeoutException)
            {
                return Fail(correlationId, ProxyErrorCode.Timeout, "Proxy stop timed out.");
            }
            catch (Exception)
            {
                return Fail(correlationId, ProxyErrorCode.StopFailed, "Proxy stop failed.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<ProxyStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_snapshot);
    }

    private ProxyResult Fail(string correlationId, ProxyErrorCode code, string message)
    {
        var error = new ProxyError(code, message);
        Publish(ProxyStatusKind.Failed, correlationId, error);
        return ProxyResult.Failure(ProxyStatusKind.Failed, correlationId, error);
    }

    private void Report(CoreDiagnosticStage stage, CoreDiagnosticCategory category) =>
        CoreDiagnosticReporter.ReportSafely(_diagnostics, stage, category);

    private static string GetAllowListedRuntimeMessage(ProxyErrorCode code) => code switch
    {
        ProxyErrorCode.InvalidConfiguration => "Proxy configuration is invalid.",
        ProxyErrorCode.AlreadyRunning => "Proxy runtime is already running.",
        ProxyErrorCode.NotRunning => "Proxy runtime is not running.",
        ProxyErrorCode.UnsupportedMode => "The requested proxy mode is not supported.",
        ProxyErrorCode.ProcessNotFound => "The target process is not running.",
        ProxyErrorCode.ProcessExited => "The target process exited during startup.",
        ProxyErrorCode.StopFailed => "Proxy stop failed.",
        ProxyErrorCode.Timeout => "Proxy operation timed out.",
        ProxyErrorCode.Cancelled => "Proxy operation was cancelled.",
        _ => "Proxy start failed."
    };

    private void StartProcessExitMonitoring(ProxyConfiguration configuration, string correlationId)
    {
        CancelProcessExitMonitoring();
        if (_modeController is not IProcessExitWatcher processExitWatcher)
            return;

        var cancellation = new CancellationTokenSource();
        _processExitMonitorCancellation = cancellation;
        _ = StopWhenProcessExitsAsync(processExitWatcher, configuration, correlationId, cancellation);
    }

    private async Task CleanupWhenPendingStartCompletesAsync(
        Task startTask,
        ProxyConfiguration configuration,
        string correlationId)
    {
        try
        {
            await startTask.ConfigureAwait(false);
        }
        catch
        {
            // The public start result is already typed and sanitized; cleanup still owns the boundary.
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_pendingStartTask, startTask))
                return;

            _pendingStartTask = null;
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(configuration.StopTimeout);
                await _modeController.StopAsync(cleanupTimeout.Token)
                    .WaitAsync(configuration.StopTimeout)
                    .ConfigureAwait(false);
                _activeConfiguration = null;
            }
            catch
            {
                Fail(correlationId, ProxyErrorCode.StopFailed, "Proxy cleanup failed.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopWhenProcessExitsAsync(
        IProcessExitWatcher processExitWatcher,
        ProxyConfiguration configuration,
        string correlationId,
        CancellationTokenSource cancellation)
    {
        try
        {
            await processExitWatcher.WaitForProcessExitAsync(configuration, cancellation.Token).ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested)
                await StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Normal explicit-stop or replacement cleanup.
        }
        catch (ProxyRuntimeException exception)
        {
            await PublishMonitorFailureAsync(
                    correlationId,
                    exception.Code,
                    GetAllowListedRuntimeMessage(exception.Code),
                    cancellation)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            await PublishMonitorFailureAsync(
                    correlationId,
                    ProxyErrorCode.StartFailed,
                    "Unable to observe the target process.",
                    cancellation)
                .ConfigureAwait(false);
        }
    }

    private async Task PublishMonitorFailureAsync(
        string correlationId,
        ProxyErrorCode code,
        string message,
        CancellationTokenSource cancellation)
    {
        if (cancellation.IsCancellationRequested)
            return;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_processExitMonitorCancellation, cancellation))
                return;

            var activeConfiguration = _activeConfiguration;
            if (activeConfiguration != null)
            {
                try
                {
                    using var stopTimeout = new CancellationTokenSource(activeConfiguration.StopTimeout);
                    await _modeController.StopAsync(stopTimeout.Token)
                        .WaitAsync(activeConfiguration.StopTimeout)
                        .ConfigureAwait(false);
                }
                catch
                {
                    Fail(correlationId, ProxyErrorCode.StopFailed, "Proxy cleanup failed.");
                    return;
                }
            }

            _processExitMonitorCancellation = null;
            _activeConfiguration = null;
            Fail(correlationId, code, message);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void CancelProcessExitMonitoring()
    {
        var cancellation = _processExitMonitorCancellation;
        _processExitMonitorCancellation = null;
        if (cancellation == null)
            return;

        cancellation.Cancel();
    }

    private void Publish(ProxyStatusKind status, string correlationId, ProxyError? error = null)
    {
        _snapshot = new ProxyStatusSnapshot(status, correlationId, _clock(), error);
        try
        {
            _statusSink.OnStatusChanged(new ProxyStatusEvent(status, correlationId, _snapshot.Timestamp, error));
        }
        catch
        {
            // A presentation sink must not be able to break runtime lifecycle.
        }
    }

    private sealed class NullProxyStatusSink : IProxyStatusSink
    {
        public static readonly NullProxyStatusSink Instance = new();

        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
        }
    }
}
