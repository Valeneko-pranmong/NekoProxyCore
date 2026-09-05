using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Interops;

namespace Tests.Windows;

[TestClass]
public sealed class RedirectorStatisticsInteropTests
{
    [TestMethod]
    public void RedirectorStatisticsStructSizeIs88Bytes()
    {
        Assert.AreEqual(88, Marshal.SizeOf<Redirector.RedirectorStatistics>());
    }

    [TestMethod]
    public void RedirectorStatisticsFieldOffsetsMatchNativeLayout()
    {
        Assert.AreEqual(0, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.TcpConnectTotal)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.TcpActive)));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.TcpClosedTotal)));
        Assert.AreEqual(24, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.UdpEventTotal)));
        Assert.AreEqual(32, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.DnsQueryTotal)));
        Assert.AreEqual(40, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.DnsFailureTotal)));
        Assert.AreEqual(48, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.RedirectSuccessTotal)));
        Assert.AreEqual(56, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.RedirectFailureTotal)));
        Assert.AreEqual(64, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.RxBytes)));
        Assert.AreEqual(72, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.TxBytes)));
        Assert.AreEqual(80, (int)Marshal.OffsetOf<Redirector.RedirectorStatistics>(nameof(Redirector.RedirectorStatistics.NetworkErrorTotal)));
    }

    [TestMethod]
    public void PInvokeGetStatsAndResetStatsExecuteCleanly()
    {
        try
        {
            Redirector.ResetStats();
            var stats = Redirector.GetStats();
            Assert.AreEqual((ulong)0, stats.TcpConnectTotal);
            Assert.AreEqual((uint)0, stats.TcpActive);
            Assert.AreEqual((ulong)0, stats.TcpClosedTotal);
            Assert.AreEqual((ulong)0, stats.UdpEventTotal);
            Assert.AreEqual((ulong)0, stats.DnsQueryTotal);
            Assert.AreEqual((ulong)0, stats.DnsFailureTotal);
            Assert.AreEqual((ulong)0, stats.RedirectSuccessTotal);
            Assert.AreEqual((ulong)0, stats.RedirectFailureTotal);
            Assert.AreEqual((ulong)0, stats.RxBytes);
            Assert.AreEqual((ulong)0, stats.TxBytes);
            Assert.AreEqual((ulong)0, stats.NetworkErrorTotal);
        }
        catch (DllNotFoundException)
        {
            Assert.Inconclusive("Redirector.bin is not present in test run directory.");
        }
    }
}
