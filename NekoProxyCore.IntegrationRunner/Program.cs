using NekoProxyCore.Core;
using NekoProxyCore.Legacy;
using NekoProxyCore.Windows;
using Netch;
using Netch.Controllers;


namespace NekoProxyCore.IntegrationRunner;

internal static class Program
{
    private const string DefaultProcessName = "pso2.exe";
    private const string DefaultProfileReference = "profile-0";
    private const string DefaultServerReference = "server-0";

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var processName = args.ElementAtOrDefault(0) ?? DefaultProcessName;
        var profileReference = args.ElementAtOrDefault(1) ?? DefaultProfileReference;
        var serverReference = args.ElementAtOrDefault(2) ?? DefaultServerReference;
        var runtimeRoot = AppContext.BaseDirectory;

        IProxyRuntime? runtime = null;
        try
        {
            var trafficWindowSeconds = ParseTrafficWindowSeconds(args.ElementAtOrDefault(3));
            await NetchRuntimeBootstrap.InitializeAsync(runtimeRoot).ConfigureAwait(false);
            Console.WriteLine(
                $"CONFIG profiles={Global.Settings.Profiles.Count} " +
                $"servers={Global.Settings.Server.Count} modes={Global.Modes.Count}");

            var sink = new SanitizedStatusSink();
            var engine = new NetchProcessModeEngine(new NetchProcessModeSessionResolver(), sink);
            var controller = new ProcessModeController(new WindowsProcessResolver(), engine);
            runtime = new HeadlessRuntimeCoordinator(controller, sink);
            var configuration = new ProxyConfiguration(
                ProxyModeKind.Process,
                processName,
                profileReference,
                serverReference,
                TimeSpan.FromSeconds(45),
                TimeSpan.FromSeconds(20));

            var start = await runtime
                .StartAsync(new ProxyStartRequest(configuration, "integration-0"))
                .ConfigureAwait(false);
            Console.WriteLine(
                $"START success={start.Succeeded} status={start.Status} " +
                $"error={start.Error?.Code.ToString() ?? "None"}");
            if (!start.Succeeded)
                return 2;

            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var steady = await runtime.GetStatusAsync().ConfigureAwait(false);
            Console.WriteLine($"STEADY status={steady.Status}");

            using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var socksStatus = await MainController.HttpConnectAsync(probeTimeout.Token).ConfigureAwait(false);
            Console.WriteLine($"SOCKS_PROBE success={socksStatus.HasValue}");

            var verificationStatus = steady;
            if (trafficWindowSeconds > 0)
            {
                Console.WriteLine($"TRAFFIC_WINDOW status=Ready durationSeconds={trafficWindowSeconds}");
                await Task.Delay(TimeSpan.FromSeconds(trafficWindowSeconds)).ConfigureAwait(false);
                verificationStatus = await runtime.GetStatusAsync().ConfigureAwait(false);
                Console.WriteLine($"TRAFFIC_WINDOW status=Complete runtime={verificationStatus.Status}");
            }

            var stop = await runtime.StopAsync().ConfigureAwait(false);
            Console.WriteLine(
                $"STOP success={stop.Succeeded} status={stop.Status} " +
                $"error={stop.Error?.Code.ToString() ?? "None"}");
            var repeatedStop = await runtime.StopAsync().ConfigureAwait(false);
            Console.WriteLine(
                $"STOP_AGAIN success={repeatedStop.Succeeded} status={repeatedStop.Status} " +
                $"error={repeatedStop.Error?.Code.ToString() ?? "None"}");

            var controllersCleared = MainController.ServerController == null && MainController.ModeController == null;
            Console.WriteLine($"CLEANUP controllers={(controllersCleared ? "clear" : "active")}");
            Console.WriteLine("TRAFFIC_GATE result=RequiresTargetVerification");

            return verificationStatus.Status == ProxyStatusKind.Running &&
                   socksStatus.HasValue &&
                   stop.Succeeded &&
                   repeatedStop.Succeeded &&
                   controllersCleared
                ? 0
                : 3;
        }
        catch (ProxyRuntimeException exception)
        {
            Console.WriteLine($"FATAL type=ProxyRuntimeException code={exception.Code}");
            return 4;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FATAL type={exception.GetType().Name} code=Unhandled");
            return 5;
        }
        finally
        {
            await BestEffortStopAsync(runtime).ConfigureAwait(false);
        }
    }

    private static int ParseTrafficWindowSeconds(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        return int.TryParse(value, out var seconds) && seconds is >= 0 and <= 900
            ? seconds
            : throw new ProxyRuntimeException(
                ProxyErrorCode.InvalidConfiguration,
                "The traffic verification window is invalid.");
    }


    private static async Task BestEffortStopAsync(IProxyRuntime? runtime)
    {
        if (runtime != null)
        {
            try
            {
                await runtime.StopAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        try
        {
            await MainController.StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private sealed class SanitizedStatusSink : IProxyStatusSink
    {
        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
            Console.WriteLine(
                $"EVENT status={statusEvent.Status} " +
                $"error={statusEvent.Error?.Code.ToString() ?? "None"}");
        }
    }
}
