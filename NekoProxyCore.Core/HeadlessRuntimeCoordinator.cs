namespace NekoProxyCore.Core;

public sealed class HeadlessRuntimeCoordinator : IProxyRuntime
{
    private readonly IProxyModeController _modeController;
    private readonly IProxyStatusSink _statusSink;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private ProxyStatusSnapshot _snapshot;
    private ProxyConfiguration? _activeConfiguration;

    public HeadlessRuntimeCoordinator(
        IProxyModeController modeController,
        IProxyStatusSink? statusSink = null,
        Func<DateTimeOffset>? clock = null)
    {
        _modeController = modeController ?? throw new ArgumentNullException(nameof(modeController));
        _statusSink = statusSink ?? NullProxyStatusSink.Instance;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
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
            if (_snapshot.Status is ProxyStatusKind.Starting or ProxyStatusKind.Running or ProxyStatusKind.Stopping)
                return Fail(request.CorrelationId, ProxyErrorCode.AlreadyRunning, "Proxy runtime is already running.");

            Publish(ProxyStatusKind.Starting, request.CorrelationId);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
                timeout.CancelAfter(request.Configuration.StartTimeout);
                await _modeController.StartAsync(request.Configuration, timeout.Token)
                    .WaitAsync(request.Configuration.StartTimeout, request.CancellationToken)
                    .ConfigureAwait(false);

                _activeConfiguration = request.Configuration;
                Publish(ProxyStatusKind.Running, request.CorrelationId);
                return ProxyResult.Success(ProxyStatusKind.Running, request.CorrelationId);
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.Cancelled, "Proxy start was cancelled.");
            }
            catch (OperationCanceledException)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.Timeout, "Proxy start timed out.");
            }
            catch (TimeoutException)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.Timeout, "Proxy start timed out.");
            }
            catch (ProxyRuntimeException e)
            {
                return Fail(request.CorrelationId, e.Code, e.Message);
            }
            catch (Exception e)
            {
                return Fail(request.CorrelationId, ProxyErrorCode.StartFailed, e.Message);
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
            catch (Exception e)
            {
                return Fail(correlationId, ProxyErrorCode.StopFailed, e.Message);
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
