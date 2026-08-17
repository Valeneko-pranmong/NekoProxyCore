using System.Security.Cryptography;
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
                var settingsKey = ProductionProtectedSettings.LoadKey();
                try
                {
                    await NetchRuntimeBootstrap.InitializeProtectedAsync(
                            AppContext.BaseDirectory,
                            Path.Combine(AppContext.BaseDirectory, ProtectedSettingsPayload.DefaultFileName),
                            settingsKey)
                        .ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(settingsKey);
                }
                var configurationCatalog = new NetchProcessModeConfigurationCatalog();
                var primaryStatusSink = new NullStatusSink();
                using var telemetryBuffer = new BoundedTelemetryBuffer(BoundedTelemetryBuffer.DefaultCapacity);
                var telemetryPublisher = new TelemetryPublisher(telemetryBuffer);
                var statusSink = new CompositeProxyStatusSink(primaryStatusSink, telemetryPublisher);
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
                var telemetryAggregator = new CoreTelemetryAggregator(runtime, telemetryPublisher);
                var telemetryServer = new HeadlessTelemetryServer(telemetryBuffer, diagnostics: diagnostics);

                telemetryPublisher.PublishLifecycle("core.started", "core");
                var telemetryTask = telemetryServer.RunAsync(shutdown.Token);
                var aggregatorTask = telemetryAggregator.RunAsync(shutdown.Token);

                try
                {
                    await server.RunAsync(shutdown.Token).ConfigureAwait(false);
                }
                finally
                {
                    telemetryPublisher.PublishLifecycle("core.stopping", "core");
                    await runtime.StopAsync().ConfigureAwait(false);
                    telemetryPublisher.PublishLifecycle("core.stopped", "core");
                    try
                    {
                        await Task.WhenAll(telemetryTask, aggregatorTask).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Clean host exit
                    }
                }
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
