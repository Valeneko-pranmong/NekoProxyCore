using NekoProxyCore.Core;
using Netch;
using Netch.Controllers;
using Netch.Models;
using Netch.Models.Modes.ProcessMode;
using Netch.Servers;

namespace NekoProxyCore.Legacy;

/// <summary>
/// Resolves profile-N and server-N identifiers against legacy runtime state. The resulting
/// server and mode objects never leave this assembly, because they can contain sensitive data.
/// </summary>
public sealed class NetchProcessModeSessionResolver : ILegacyProcessModeSessionResolver, IRuntimeConfiguredLegacyProcessModeSessionResolver
{
    private readonly NetchProcessModeConfigurationCatalog _configurationCatalog;
    private readonly ICoreDiagnosticSink _diagnostics;

    public NetchProcessModeSessionResolver(ICoreDiagnosticSink? diagnostics = null) =>
        (_configurationCatalog, _diagnostics) = (
            new NetchProcessModeConfigurationCatalog(),
            diagnostics ?? NullCoreDiagnosticSink.Instance);

    public NetchProcessModeSessionResolver(
        NetchProcessModeConfigurationCatalog configurationCatalog,
        ICoreDiagnosticSink? diagnostics = null) =>
        (_configurationCatalog, _diagnostics) = (
            configurationCatalog ?? throw new ArgumentNullException(nameof(configurationCatalog)),
            diagnostics ?? NullCoreDiagnosticSink.Instance);

    public Task<ILegacyProcessModeSession> ResolveAsync(
        ProxyConfiguration configuration,
        IProxyStatusSink statusSink,
        CancellationToken cancellationToken)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        if (statusSink == null)
            throw new ArgumentNullException(nameof(statusSink));

        cancellationToken.ThrowIfCancellationRequested();
        if (configuration.Mode != ProxyModeKind.Process)
            throw new ProxyRuntimeException(ProxyErrorCode.UnsupportedMode, "The requested proxy mode is not supported.");

        var resolution = _configurationCatalog.Resolve(
            configuration.ProfileReference,
            configuration.ServerReference);
        if (!resolution.Valid)
            ThrowInvalidConfiguration(resolution.Failure);

        ILegacyProcessModeSession session = new NetchProcessModeSession(
            resolution.Server!,
            resolution.Mode!,
            resolution.RuntimeSettings!,
            statusSink);
        Report(CoreDiagnosticCategory.StageCompleted);
        return Task.FromResult(session);
    }

    public Task<ILegacyProcessModeSession> ResolveAsync(ProxyConfiguration configuration, RuntimeProxyConfig runtimeConfig, IProxyStatusSink statusSink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(runtimeConfig);
        ArgumentNullException.ThrowIfNull(statusSink);
        cancellationToken.ThrowIfCancellationRequested();
        if (configuration.Mode != ProxyModeKind.Process)
            throw new ProxyRuntimeException(ProxyErrorCode.UnsupportedMode, "The requested proxy mode is not supported.");
        var resolution = _configurationCatalog.Resolve(configuration.ProfileReference, configuration.ServerReference);
        if (!resolution.Valid)
            ThrowInvalidConfiguration(resolution.Failure);
        if (resolution.Server is not ShadowsocksServer server || server.GetType() != typeof(ShadowsocksServer) ||
            runtimeConfig.Protocol != "shadowsocks" ||
            !SSGlobal.EncryptMethods.Contains(runtimeConfig.Cipher, StringComparer.Ordinal) || runtimeConfig.Port is < 1 or > ushort.MaxValue)
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "Proxy configuration is invalid.");
        ILegacyProcessModeSession session = new NetchProcessModeSession(
            server, resolution.Mode!, resolution.RuntimeSettings!, statusSink, runtimeConfig);
        Report(CoreDiagnosticCategory.StageCompleted);
        return Task.FromResult(session);
    }

    private void ThrowInvalidConfiguration(ProcessModeConfigurationResolutionFailure failure)
    {
        var category = failure switch
        {
            ProcessModeConfigurationResolutionFailure.ProfileReferenceInvalid =>
                CoreDiagnosticCategory.SessionProfileReferenceInvalid,
            ProcessModeConfigurationResolutionFailure.ServerReferenceInvalid =>
                CoreDiagnosticCategory.SessionServerReferenceInvalid,
            ProcessModeConfigurationResolutionFailure.ProfileNotFound or
                ProcessModeConfigurationResolutionFailure.ProfileAmbiguous =>
                CoreDiagnosticCategory.SessionProfileNotFound,
            ProcessModeConfigurationResolutionFailure.ServerNotFound =>
                CoreDiagnosticCategory.SessionServerNotFound,
            ProcessModeConfigurationResolutionFailure.ProfileServerMismatch =>
                CoreDiagnosticCategory.SessionProfileServerMismatch,
            ProcessModeConfigurationResolutionFailure.ModeNotFound =>
                CoreDiagnosticCategory.SessionModeNotFound,
            ProcessModeConfigurationResolutionFailure.ModeAmbiguous =>
                CoreDiagnosticCategory.SessionModeAmbiguous,
            _ => CoreDiagnosticCategory.SessionModeNotFound
        };

        Report(category);
        throw new ProxyRuntimeException(
            ProxyErrorCode.InvalidConfiguration,
            "The ProcessMode configuration could not be resolved.");
    }

    private void Report(CoreDiagnosticCategory category) =>
        CoreDiagnosticReporter.ReportSafely(
            _diagnostics,
            CoreDiagnosticStage.SessionResolve,
            category);

    private sealed class NetchProcessModeSession : ILegacyProcessModeSession
    {
        private static readonly SemaphoreSlim GlobalSettingsLease = new(1, 1);
        // Private test seam: production callers cannot replace lifecycle behavior.
        private static Func<Server, Redirector, IProxyStatusSink, Task> StartControllerAsync =
            (server, mode, sink) => MainController.StartAsync(server, mode, sink, openLogOnUnhandledException: false);
        private static Func<Task> StopControllerAsync = MainController.StopAsync;
        private readonly Server _server;
        private readonly Redirector _mode;
        private readonly Setting _runtimeSettings;
        private readonly IProxyStatusSink _statusSink;
        private readonly object _stateGate = new();
        private RuntimeProxyConfig? _runtimeConfig;
        private Setting? _liveSettings;
        private CancellationTokenSource? _leaseWaitCancellation;
        private Task? _startTask;
        private Task? _stopTask;
        private bool _leaseHeld;
        private bool _nativeStartInvoked;
        private bool _teardownSucceeded;

        public NetchProcessModeSession(
            Server server,
            Redirector mode,
            Setting runtimeSettings,
            IProxyStatusSink statusSink,
            RuntimeProxyConfig? runtimeConfig = null)
        {
            _server = server;
            _mode = mode;
            _runtimeSettings = runtimeSettings;
            _statusSink = statusSink;
            _runtimeConfig = runtimeConfig;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Task startTask;
            CancellationTokenSource leaseWaitCancellation;
            lock (_stateGate)
            {
                if (!_leaseHeld && (_startTask?.IsCanceled == true || _stopTask?.IsCompleted == true))
                {
                    _leaseWaitCancellation?.Dispose();
                    _leaseWaitCancellation = null;
                    _startTask = null;
                    _stopTask = null;
                }
                if (_startTask == null)
                {
                    _leaseWaitCancellation = new CancellationTokenSource();
                    _startTask = RunStartAsync(_leaseWaitCancellation.Token);
                    ObserveFault(_startTask);
                }
                startTask = _startTask;
                leaseWaitCancellation = _leaseWaitCancellation!;
            }
            return WaitForStartAsync(startTask, leaseWaitCancellation, cancellationToken);
        }

        private async Task WaitForStartAsync(
            Task startTask,
            CancellationTokenSource leaseWaitCancellation,
            CancellationToken cancellationToken)
        {
            try
            {
                await startTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_stateGate)
                {
                    if (!_leaseHeld)
                    {
                        ClearSensitiveReferencesLocked();
                        leaseWaitCancellation.Cancel();
                    }
                }
                throw;
            }
            catch
            {
                await GetOrCreateStopTaskAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task RunStartAsync(CancellationToken leaseWaitCancellation)
        {
            try
            {
                await GlobalSettingsLease.WaitAsync(leaseWaitCancellation).ConfigureAwait(false);
            }
            catch
            {
                lock (_stateGate)
                    ClearSensitiveReferencesLocked();
                throw;
            }

            lock (_stateGate)
            {
                _leaseHeld = true;
                _liveSettings = Global.Settings;
                InjectRuntimeConfigurationLocked();
                Global.Settings = _runtimeSettings;
                _nativeStartInvoked = true;
            }

            // MainController.StartAsync( is the production delegate target above.
            await StartControllerAsync(_server, _mode, _statusSink).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Task stopTask;
            lock (_stateGate)
            {
                if (_teardownSucceeded || _startTask == null)
                {
                    ClearSensitiveReferencesLocked();
                    return Task.CompletedTask;
                }
                if (!_leaseHeld && !_startTask.IsCompleted)
                    _leaseWaitCancellation?.Cancel();
                stopTask = GetOrCreateStopTaskLockedAsync();
            }
            return stopTask.WaitAsync(cancellationToken);
        }

        private Task GetOrCreateStopTaskAsync()
        {
            lock (_stateGate)
                return GetOrCreateStopTaskLockedAsync();
        }

        private Task GetOrCreateStopTaskLockedAsync()
        {
            if (_stopTask == null || _stopTask.IsFaulted || _stopTask.IsCanceled)
            {
                _stopTask = RunStopAttemptAsync(_startTask!);
                ObserveFault(_stopTask);
            }
            return _stopTask;
        }

        private async Task RunStopAttemptAsync(Task startTask)
        {
#pragma warning disable VSTHRD003 // This is the session-owned native start operation.
            try { await startTask.ConfigureAwait(false); }
            catch { /* An invoked native start still requires native teardown. */ }
#pragma warning restore VSTHRD003

            bool shouldStop;
            lock (_stateGate)
                shouldStop = _leaseHeld && _nativeStartInvoked && !_teardownSucceeded;
            if (!shouldStop)
            {
                lock (_stateGate)
                    ClearSensitiveReferencesLocked();
                return;
            }

            try
            {
                await StopControllerAsync().ConfigureAwait(false);
            }
            catch
            {
                lock (_stateGate)
                    ClearPasswordReferenceLocked();
                throw;
            }

            lock (_stateGate)
            {
                if (_teardownSucceeded)
                    return;
                ClearPasswordReferenceLocked();
                Global.Settings = _liveSettings!;
                _liveSettings = null;
                _leaseHeld = false;
                _nativeStartInvoked = false;
                _teardownSucceeded = true;
                GlobalSettingsLease.Release();
            }
        }

        private void InjectRuntimeConfigurationLocked()
        {
            if (_runtimeConfig == null)
                return;
            var server = (ShadowsocksServer)_server;
            server.Hostname = _runtimeConfig.Host;
            server.Port = checked((ushort)_runtimeConfig.Port);
            server.EncryptMethod = _runtimeConfig.Cipher;
            server.Password = _runtimeConfig.Credential.RevealForTransport();
            _runtimeConfig = null;
        }

        private void ClearSensitiveReferencesLocked()
        {
            _runtimeConfig = null;
            ClearPasswordReferenceLocked();
        }

        private void ClearPasswordReferenceLocked()
        {
            if (_server is ShadowsocksServer shadowsocks)
            {
                try { shadowsocks.Password = string.Empty; }
                catch { }
            }
        }

        private static void ObserveFault(Task task) =>
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
