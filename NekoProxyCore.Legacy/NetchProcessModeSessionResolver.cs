using NekoProxyCore.Core;
using Netch.Controllers;
using Netch.Models;
using Netch.Models.Modes.ProcessMode;

namespace NekoProxyCore.Legacy;

/// <summary>
/// Resolves profile-N and server-N identifiers against legacy runtime state. The resulting
/// server and mode objects never leave this assembly, because they can contain sensitive data.
/// </summary>
public sealed class NetchProcessModeSessionResolver : ILegacyProcessModeSessionResolver
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
            statusSink);
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
        private readonly Server _server;
        private readonly Redirector _mode;
        private readonly IProxyStatusSink _statusSink;

        public NetchProcessModeSession(Server server, Redirector mode, IProxyStatusSink statusSink)
        {
            _server = server;
            _mode = mode;
            _statusSink = statusSink;
        }

        public Task StartAsync(CancellationToken cancellationToken) =>
            MainController.StartAsync(_server, _mode, _statusSink, openLogOnUnhandledException: false).WaitAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) =>
            MainController.StopAsync().WaitAsync(cancellationToken);
    }
}
