using NekoProxyCore.Core;
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
        using (var shutdown = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };

            var statusSink = new NullStatusSink();
            var engine = new NetchProcessModeEngine(new NetchProcessModeSessionResolver(), statusSink);
            var controller = new ProcessModeController(new WindowsProcessResolver(), engine);

            // The release composition pins NEKO-AUTH-S0 and remains fail closed until
            // approved immutable public keys and trusted-clock material are supplied.
            var startAuthorizer = ProductionAuthorizationComposition.CreateStartAuthorizer();
            var runtime = new HeadlessRuntimeCoordinator(controller, startAuthorizer, statusSink);
            var server = new HeadlessControlServer(runtime, new CoreChallengeService());
            try
            {
                await NetchRuntimeBootstrap.InitializeAsync(AppContext.BaseDirectory).ConfigureAwait(false);
                await server.RunAsync(shutdown.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                await runtime.StopAsync().ConfigureAwait(false);
                return 0;
            }
            catch
            {
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
