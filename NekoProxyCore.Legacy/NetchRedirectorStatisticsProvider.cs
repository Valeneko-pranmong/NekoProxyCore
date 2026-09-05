#if WINDOWS
using NekoProxyCore.Core;
using Netch.Interops;

namespace NekoProxyCore.Legacy;

public sealed class NetchRedirectorStatisticsProvider : INetFilterStatisticsProvider
{
    public NetFilterStatisticsSnapshot GetCurrentStatistics()
    {
        try
        {
            var raw = Redirector.GetStats();
            return new NetFilterStatisticsSnapshot(
                TcpConnectTotal: raw.TcpConnectTotal,
                TcpActive: raw.TcpActive,
                TcpClosedTotal: raw.TcpClosedTotal,
                UdpEventTotal: raw.UdpEventTotal,
                DnsQueryTotal: raw.DnsQueryTotal,
                DnsFailureTotal: raw.DnsFailureTotal,
                RedirectSuccessTotal: raw.RedirectSuccessTotal,
                RedirectFailureTotal: raw.RedirectFailureTotal,
                RxBytes: raw.RxBytes,
                TxBytes: raw.TxBytes,
                NetworkErrorTotal: raw.NetworkErrorTotal);
        }
        catch
        {
            return default;
        }
    }
}
#endif
