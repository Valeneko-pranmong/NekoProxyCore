namespace NekoProxyCore.Core;

public interface INetFilterStatisticsProvider
{
    NetFilterStatisticsSnapshot GetCurrentStatistics();
}

public readonly record struct NetFilterStatisticsSnapshot(
    ulong TcpConnectTotal,
    uint TcpActive,
    ulong TcpClosedTotal,
    ulong UdpEventTotal,
    ulong DnsQueryTotal,
    ulong DnsFailureTotal,
    ulong RedirectSuccessTotal,
    ulong RedirectFailureTotal,
    ulong RxBytes,
    ulong TxBytes,
    ulong NetworkErrorTotal);

public sealed class NullNetFilterStatisticsProvider : INetFilterStatisticsProvider
{
    public static readonly NullNetFilterStatisticsProvider Instance = new();

    public NetFilterStatisticsSnapshot GetCurrentStatistics() => default;
}
