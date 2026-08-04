using NekoProxyCore.Core;

namespace NekoProxyCore.Legacy;

/// <summary>
/// IProcessModeEngine adapter for the legacy Netch ProcessMode lifecycle.
/// It accepts only Core's sanitized identifiers; configuration resolution remains runtime-only.
/// </summary>
public sealed class NetchProcessModeEngine : IProcessModeEngine
{
    private readonly ILegacyProcessModeSessionResolver _sessionResolver;
    private readonly IProxyStatusSink _statusSink;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private ILegacyProcessModeSession? _activeSession;

    public NetchProcessModeEngine(
        ILegacyProcessModeSessionResolver sessionResolver,
        IProxyStatusSink? statusSink = null)
    {
        _sessionResolver = sessionResolver ?? throw new ArgumentNullException(nameof(sessionResolver));
        _statusSink = statusSink ?? NullProxyStatusSink.Instance;
    }

    public async Task StartAsync(ProxyConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeSession != null)
                throw new ProxyRuntimeException(ProxyErrorCode.AlreadyRunning, "The legacy ProcessMode engine is already running.");

            Publish(ProxyStatusKind.Starting);
            try
            {
                var session = await _sessionResolver
                    .ResolveAsync(configuration, _statusSink, cancellationToken)
                    .ConfigureAwait(false);
                _activeSession = session ?? throw new ProxyRuntimeException(
                    ProxyErrorCode.InvalidConfiguration,
                    "The ProcessMode profile could not be resolved.");

                await _activeSession.StartAsync(cancellationToken).ConfigureAwait(false);
                Publish(ProxyStatusKind.Running);
            }
            catch (OperationCanceledException)
            {
                await CleanupFailedStartAsync(configuration.StopTimeout).ConfigureAwait(false);
                PublishFailure(ProxyErrorCode.Cancelled, "Legacy ProcessMode startup was cancelled.");
                throw;
            }
            catch (ProxyRuntimeException exception)
            {
                await CleanupFailedStartAsync(configuration.StopTimeout).ConfigureAwait(false);
                PublishFailure(exception.Code, exception.Message);
                throw;
            }
            catch (Exception)
            {
                await CleanupFailedStartAsync(configuration.StopTimeout).ConfigureAwait(false);
                const string message = "The legacy ProcessMode engine could not be started.";
                PublishFailure(ProxyErrorCode.StartFailed, message);
                throw new ProxyRuntimeException(ProxyErrorCode.StartFailed, message);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeSession == null)
                return;

            Publish(ProxyStatusKind.Stopping);
            try
            {
                await _activeSession.StopAsync(cancellationToken).ConfigureAwait(false);
                _activeSession = null;
                Publish(ProxyStatusKind.Stopped);
            }
            catch (OperationCanceledException)
            {
                PublishFailure(ProxyErrorCode.Cancelled, "Legacy ProcessMode shutdown was cancelled.");
                throw;
            }
            catch (ProxyRuntimeException exception)
            {
                PublishFailure(exception.Code, exception.Message);
                throw;
            }
            catch (Exception)
            {
                const string message = "The legacy ProcessMode engine could not be stopped.";
                PublishFailure(ProxyErrorCode.StopFailed, message);
                throw new ProxyRuntimeException(ProxyErrorCode.StopFailed, message);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task CleanupFailedStartAsync(TimeSpan stopTimeout)
    {
        var session = _activeSession;
        _activeSession = null;
        if (session == null)
            return;

        try
        {
            await session.StopAsync(CancellationToken.None).WaitAsync(stopTimeout).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original startup result. The coordinator will return a typed error.
        }
    }

    private void PublishFailure(ProxyErrorCode code, string message)
    {
        var error = new ProxyError(code, message);
        Publish(new ProxyStatusEvent(ProxyStatusKind.Failed, string.Empty, DateTimeOffset.UtcNow, error));
    }

    private void Publish(ProxyStatusKind status)
    {
        Publish(new ProxyStatusEvent(status, string.Empty, DateTimeOffset.UtcNow));
    }

    private void Publish(ProxyStatusEvent statusEvent)
    {
        try
        {
            _statusSink.OnStatusChanged(statusEvent);
        }
        catch
        {
            // Presentation code must not be able to break the proxy lifecycle.
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
