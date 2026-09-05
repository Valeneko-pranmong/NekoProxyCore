using System;
using System.Threading;
using System.Threading.Tasks;
using NekoProxyCore.Core;
using Netch.Controllers;
using Netch.Models;

namespace NekoProxyCore.Legacy;

public sealed class LegacyProxyRttProducer : IProxyRttProducer
{
    private int? _lastRtt;
    private DateTimeOffset _lastProbeTime = DateTimeOffset.MinValue;
    private Server? _lastServer;
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Func<Server, Task<int>> PingFunc { get; set; } = s => s.PingAsync();
    public Func<Server?> GetServerFunc { get; set; } = () => MainController.Server;
    public Func<DateTimeOffset> GetUtcNowFunc { get; set; } = () => DateTimeOffset.UtcNow;

    public async Task<int?> GetRttAsync(CancellationToken cancellationToken = default)
    {
        var server = GetServerFunc();
        if (server == null || string.IsNullOrWhiteSpace(server.Hostname))
            return null;

        var now = GetUtcNowFunc();
        if (ReferenceEquals(_lastServer, server) && (now - _lastProbeTime < Cadence))
            return _lastRtt;

        if (!await _lock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return _lastRtt;

        try
        {
            // Re-check inside lock
            if (ReferenceEquals(_lastServer, server) && (GetUtcNowFunc() - _lastProbeTime < Cadence))
                return _lastRtt;

            var rtt = await PingFunc(server).ConfigureAwait(false);
            _lastRtt = rtt >= 0 && rtt < 1000 ? rtt : null;
            _lastServer = server;
            _lastProbeTime = GetUtcNowFunc();
        }
        catch
        {
            _lastRtt = null;
            _lastServer = server;
            _lastProbeTime = GetUtcNowFunc();
        }
        finally
        {
            _lock.Release();
        }

        return _lastRtt;
    }
}
