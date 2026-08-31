using System;
using System.Threading;
using System.Threading.Tasks;
using NekoProxyCore.Core;
using Netch.Controllers;
using Netch.Utils;

namespace NekoProxyCore.Legacy;

public sealed class LegacyProxyRttProducer : IProxyRttProducer
{
    private int? _lastRtt;
    private DateTimeOffset _lastProbeTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<int?> GetRttAsync(CancellationToken cancellationToken = default)
    {
        var server = MainController.Server;
        if (server == null || string.IsNullOrWhiteSpace(server.Hostname))
            return null;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastProbeTime < Cadence)
            return _lastRtt;

        if (!await _lock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return _lastRtt;

        try
        {
            var destination = await DnsUtils.LookupAsync(server.Hostname).ConfigureAwait(false);
            if (destination == null)
            {
                _lastRtt = null;
                _lastProbeTime = DateTimeOffset.UtcNow;
                return null;
            }

            var rtt = await Utils.TCPingAsync(destination, server.Port, 1000, cancellationToken).ConfigureAwait(false);
            _lastRtt = rtt >= 0 && rtt < 1000 ? rtt : null;
            _lastProbeTime = DateTimeOffset.UtcNow;
        }
        catch
        {
            _lastRtt = null;
            _lastProbeTime = DateTimeOffset.UtcNow;
        }
        finally
        {
            _lock.Release();
        }

        return _lastRtt;
    }
}
