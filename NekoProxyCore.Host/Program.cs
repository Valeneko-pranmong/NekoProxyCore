using NekoProxyCore.Core;
using NekoProxyCore.Host.Authorization;
#if WINDOWS
using NekoProxyCore.Legacy;
using NekoProxyCore.Windows;
#endif

namespace NekoProxyCore.Host;

internal static class Program
{
    private static async Task<int> Main()
    {
#if !WINDOWS
        await Task.CompletedTask;
        return 2;
#else
        if (!SingleInstanceLease.TryAcquire(out var lease))
            return 3;

        using (lease)
        using (var shutdown = new HostShutdownSignal())
        {
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.RequestShutdown();
            };

            HeadlessRuntimeCoordinator? runtime = null;
            try
            {
                var diagnostics = HostDiagnosticSink.Create();
                // Missing or invalid bundled trust material terminates the host before runtime
                // initialization; there is no authorization fallback in the production entry point.
                var startAuthorizer = ProductionAuthorizationComposition.CreateStartAuthorizer(
                    ProductionPublicKeys.LoadBundled(), null, diagnostics);
                await NetchRuntimeBootstrap.InitializeAsync(AppContext.BaseDirectory).ConfigureAwait(false);
                var configurationCatalog = new NetchProcessModeConfigurationCatalog();
                var statusSink = new NullStatusSink();
                var engine = new NetchProcessModeEngine(
                    new NetchProcessModeSessionResolver(configurationCatalog, diagnostics),
                    statusSink,
                    diagnostics);
                var controller = new ProcessModeController(new WindowsProcessResolver(), engine, diagnostics);
                runtime = new HeadlessRuntimeCoordinator(
                    controller, startAuthorizer, statusSink, null, diagnostics);
                var server = new HeadlessControlServer(
                    runtime,
                    new CoreChallengeService(),
                    shutdown,
                    configurationCatalog,
                    diagnostics: diagnostics);

                await server.RunAsync(shutdown.Token).ConfigureAwait(false);
                await runtime.StopAsync().ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (shutdown.IsShutdownRequested)
            {
                if (runtime != null)
                    await runtime.StopAsync().ConfigureAwait(false);
                return 0;
            }
            catch
            {
                if (runtime != null)
                    await runtime.StopAsync().ConfigureAwait(false);
                return 2;
            }
        }
#endif
    }

    private sealed class NullStatusSink : IProxyStatusSink
    {
        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
        }
    }
}
