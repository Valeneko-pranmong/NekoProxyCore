using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class CoreChallengeServiceTests
{
    [TestMethod]
    public void IssueCreatesChallengeWithAtLeast256BitsOfEntropy()
    {
        var service = new CoreChallengeService();

        var challenge = service.Issue();
        var encoded = challenge.Value
            .Replace('-', '+')
            .Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        var decoded = Convert.FromBase64String(encoded);

        Assert.IsTrue(decoded.Length >= 32);
    }

    [TestMethod]
    public void ConsumeRejectsChallengeAfterMonotonicDeadline()
    {
        var clock = new FakeMonotonicClock();
        var service = new CoreChallengeService(clock, TimeSpan.FromSeconds(30));
        var challenge = service.Issue();
        clock.Advance(TimeSpan.FromSeconds(31));

        var result = service.ConsumeForAttempt(challenge.Value);

        Assert.AreEqual(ChallengeConsumption.Expired, result);
    }

    [TestMethod]
    public void ConsumeRejectsChallengeAtMonotonicDeadline()
    {
        var clock = new FakeMonotonicClock();
        var service = new CoreChallengeService(clock, TimeSpan.FromSeconds(30));
        var challenge = service.Issue();
        clock.Advance(TimeSpan.FromSeconds(30));

        var result = service.ConsumeForAttempt(challenge.Value);

        Assert.AreEqual(ChallengeConsumption.Expired, result);
    }

    [TestMethod]
    public void FailedFirstAttemptConsumesOutstandingChallenge()
    {
        var service = new CoreChallengeService();
        var challenge = service.Issue();

        var invalid = service.ConsumeForAttempt("invalid-challenge-attempt");
        var replay = service.ConsumeForAttempt(challenge.Value);

        Assert.AreEqual(ChallengeConsumption.Invalid, invalid);
        Assert.AreEqual(ChallengeConsumption.Replayed, replay);
    }

    [TestMethod]
    public void NewChallengeReplacesThePreviousOutstandingChallenge()
    {
        var service = new CoreChallengeService();
        var first = service.Issue();
        var second = service.Issue();

        var oldAttempt = service.ConsumeForAttempt(first.Value);

        Assert.AreNotEqual(first.Value, second.Value);
        Assert.AreEqual(ChallengeConsumption.Invalid, oldAttempt);
        Assert.AreEqual(ChallengeConsumption.Replayed, service.ConsumeForAttempt(second.Value));
    }

    [TestMethod]
    public async Task ConcurrentConsumersAcceptOutstandingChallengeAtMostOnce()
    {
        var service = new CoreChallengeService();
        var challenge = service.Issue();
        using var ready = new CountdownEvent(16);
        using var start = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, 16)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    return service.ConsumeForAttempt(challenge.Value);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();
        Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(2)));

        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.AreEqual(1, results.Count(result => result == ChallengeConsumption.Accepted));
        Assert.AreEqual(15, results.Count(result => result == ChallengeConsumption.Replayed));
    }

    private sealed class FakeMonotonicClock : IMonotonicClock
    {
        private long _timestamp;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromTicks(endingTimestamp - startingTimestamp);

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
