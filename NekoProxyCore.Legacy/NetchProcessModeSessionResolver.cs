using System.Globalization;
using NekoProxyCore.Core;
using Netch;
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
    private const string ProfilePrefix = "profile-";
    private const string ServerPrefix = "server-";
    private readonly ICoreDiagnosticSink _diagnostics;

    public NetchProcessModeSessionResolver(ICoreDiagnosticSink? diagnostics = null) =>
        _diagnostics = diagnostics ?? NullCoreDiagnosticSink.Instance;

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

        var profileIndex = ParseReference(
            configuration.ProfileReference,
            ProfilePrefix,
            "profile",
            CoreDiagnosticCategory.SessionProfileReferenceInvalid);
        var serverIndex = ParseReference(
            configuration.ServerReference,
            ServerPrefix,
            "server",
            CoreDiagnosticCategory.SessionServerReferenceInvalid);

        var profile = Global.Settings.Profiles.SingleOrDefault(item => item.Index == profileIndex);
        if (profile == null)
        {
            Report(CoreDiagnosticCategory.SessionProfileNotFound);
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "The ProcessMode profile could not be resolved.");
        }

        if (serverIndex >= Global.Settings.Server.Count)
        {
            Report(CoreDiagnosticCategory.SessionServerNotFound);
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "The ProcessMode server could not be resolved.");
        }

        var server = Global.Settings.Server[serverIndex];
        if (!string.Equals(profile.ServerRemark, server.Remark, StringComparison.Ordinal))
        {
            Report(CoreDiagnosticCategory.SessionProfileServerMismatch);
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "The ProcessMode profile does not match the selected server.");
        }

        var matchingModes = Global.Modes
            .OfType<Redirector>()
            .Where(mode => mode.Remark.Values.Any(value => string.Equals(value, profile.ModeRemark, StringComparison.Ordinal)))
            .Take(2)
            .ToArray();
        if (matchingModes.Length == 0)
        {
            Report(CoreDiagnosticCategory.SessionModeNotFound);
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "The ProcessMode configuration could not be resolved.");
        }
        if (matchingModes.Length > 1)
        {
            Report(CoreDiagnosticCategory.SessionModeAmbiguous);
            throw new ProxyRuntimeException(ProxyErrorCode.InvalidConfiguration, "The ProcessMode configuration could not be resolved.");
        }

        ILegacyProcessModeSession session = new NetchProcessModeSession(server, matchingModes[0], statusSink);
        Report(CoreDiagnosticCategory.StageCompleted);
        return Task.FromResult(session);
    }

    private int ParseReference(
        string reference,
        string prefix,
        string kind,
        CoreDiagnosticCategory invalidCategory)
    {
        if (!reference.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(reference[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index < 0)
        {
            Report(invalidCategory);
            throw new ProxyRuntimeException(
                ProxyErrorCode.InvalidConfiguration,
                $"The ProcessMode {kind} reference is invalid.");
        }

        return index;
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
