using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Legacy;
using Netch.Models;

namespace Tests.Windows;

[TestClass]
public sealed class LegacyProxyRttProducerTests
{
    private class DummyServer : Server
    {
        public override string Type => "Dummy";
        public override string MaskedData() => "";
    }

    [TestMethod]
    public async Task GetRttAsync_UsesPingAsync_AndRespectsCadence()
    {
        var server = new DummyServer { Hostname = "test.server" };
        var producer = new LegacyProxyRttProducer();

        int pingCallCount = 0;
        producer.PingFunc = s =>
        {
            Assert.AreSame(server, s);
            Interlocked.Increment(ref pingCallCount);
            return Task.FromResult(42);
        };
        producer.GetServerFunc = () => server;

        var time = DateTimeOffset.UtcNow;
        producer.GetUtcNowFunc = () => time;

        var rtt1 = await producer.GetRttAsync();
        Assert.AreEqual(42, rtt1);
        Assert.AreEqual(1, pingCallCount);

        var rtt2 = await producer.GetRttAsync();
        Assert.AreEqual(42, rtt2);
        Assert.AreEqual(1, pingCallCount); // Cached

        time += TimeSpan.FromSeconds(11);
        var rtt3 = await producer.GetRttAsync();
        Assert.AreEqual(42, rtt3);
        Assert.AreEqual(2, pingCallCount); // Reprobed
    }

    [TestMethod]
    public async Task GetRttAsync_Reprobes_OnIdentityChange()
    {
        var server1 = new DummyServer { Hostname = "test.server1" };
        var server2 = new DummyServer { Hostname = "test.server2" };

        var producer = new LegacyProxyRttProducer();

        int pingCallCount = 0;
        producer.PingFunc = s => { Interlocked.Increment(ref pingCallCount); return Task.FromResult(100); };

        var currentServer = server1;
        producer.GetServerFunc = () => currentServer;

        var time = DateTimeOffset.UtcNow;
        producer.GetUtcNowFunc = () => time;

        await producer.GetRttAsync();
        Assert.AreEqual(1, pingCallCount);

        currentServer = server2;
        await producer.GetRttAsync();
        Assert.AreEqual(2, pingCallCount); // Reprobed immediately without waiting for cadence
    }

    [TestMethod]
    public async Task GetRttAsync_NullServer_ReturnsNull()
    {
        var producer = new LegacyProxyRttProducer();
        producer.GetServerFunc = () => null;

        var rtt = await producer.GetRttAsync();
        Assert.IsNull(rtt);
    }

    [TestMethod]
    public async Task GetRttAsync_NegativePing_ReturnsNull()
    {
        var server = new DummyServer { Hostname = "test.server" };
        var producer = new LegacyProxyRttProducer();

        producer.PingFunc = s => Task.FromResult(-2);
        producer.GetServerFunc = () => server;
        producer.GetUtcNowFunc = () => DateTimeOffset.UtcNow;

        var rtt = await producer.GetRttAsync();
        Assert.IsNull(rtt);
    }

    [TestMethod]
    public async Task GetRttAsync_Exception_ReturnsNull_AndIsIsolated()
    {
        var server = new DummyServer { Hostname = "test.server" };
        var producer = new LegacyProxyRttProducer();

        producer.PingFunc = s => throw new Exception("Network error");
        producer.GetServerFunc = () => server;
        producer.GetUtcNowFunc = () => DateTimeOffset.UtcNow;

        var rtt = await producer.GetRttAsync();
        Assert.IsNull(rtt);
    }
}
