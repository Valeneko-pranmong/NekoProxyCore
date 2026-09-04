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
        if (resolution.Server is not ShadowsocksServer server || runtimeConfig.Protocol != "shadowsocks" ||
            !SSGlobal.EncryptMethods.Contains(runtimeConfig.Cipher, StringComparer.Ordinal) || runtimeConfig.Port is < 1 or > ushort.MaxValue)
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "Proxy configuration is invalid.");
        server.Hostname = runtimeConfig.Host;
        server.Port = checked((ushort)runtimeConfig.Port);
        server.EncryptMethod = runtimeConfig.Cipher;
        server.Password = runtimeConfig.Credential.RevealForTransport();
        ILegacyProcessModeSession session = new NetchProcessModeSession(server, resolution.Mode!, resolution.RuntimeSettings!, statusSink);
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
        private Setting? _liveSettings;
        private bool _leaseHeld;
        private readonly SemaphoreSlim _cleanupGate = new(1, 1);

        public NetchProcessModeSession(
            Server server,
            Redirector mode,
            Setting runtimeSettings,
            IProxyStatusSink statusSink)
        {
            _server = server;
            _mode = mode;
            _runtimeSettings = runtimeSettings;
            _statusSink = statusSink;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await GlobalSettingsLease.WaitAsync(cancellationToken).ConfigureAwait(false);
            _leaseHeld = true;
            _liveSettings = Global.Settings;
            Global.Settings = _runtimeSettings;
            try
            {
                // MainController.StartAsync( is the production delegate target above.
                await StartControllerAsync(_server, _mode, _statusSink)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => CleanupAsync(cancellationToken);

        private async Task CleanupAsync(CancellationToken cancellationToken)
        {
            await _cleanupGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!_leaseHeld)
                    return;
                try
                {
                    await StopControllerAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (_server is ShadowsocksServer shadowsocks)
                        {
                            try { shadowsocks.Password = string.Empty; }
                            catch { }
                        }
                    }
                    finally
                    {
                        if (_liveSettings != null)
                            Global.Settings = _liveSettings;
                        _liveSettings = null;
                        _leaseHeld = false;
                        GlobalSettingsLease.Release();
                    }
                }
            }
            finally
            {
                _cleanupGate.Release();
            }
        }
    }
}
