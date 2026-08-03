using System.Diagnostics;
using System.Security.Cryptography;

namespace NekoProxyCore.Core;

public sealed record CoreChallenge(string Value);

public enum ChallengeConsumption
{
    Accepted,
    Invalid,
    Expired,
    Replayed
}

public interface IMonotonicClock
{
    long GetTimestamp();

    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);
}

public interface ICoreChallengeService
{
    CoreChallenge Issue();

    ChallengeConsumption ConsumeForAttempt(string challenge);
}

public sealed class CoreChallengeService : ICoreChallengeService
{
    private const int ChallengeByteCount = 32;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromSeconds(30);

    private readonly IMonotonicClock _clock;
    private readonly TimeSpan _lifetime;
    private readonly object _gate = new();
    private OutstandingChallenge? _outstanding;

    public CoreChallengeService()
        : this(new StopwatchMonotonicClock(), MaximumLifetime)
    {
    }

    public CoreChallengeService(IMonotonicClock clock, TimeSpan lifetime)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        _lifetime = lifetime;
    }

    public CoreChallenge Issue()
    {
        var bytes = new byte[ChallengeByteCount];
        RandomNumberGenerator.Fill(bytes);
        var value = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        lock (_gate)
            _outstanding = new OutstandingChallenge(value, _clock.GetTimestamp());

        return new CoreChallenge(value);
    }

    public ChallengeConsumption ConsumeForAttempt(string challenge)
    {
        if (challenge is null)
            throw new ArgumentNullException(nameof(challenge));

        lock (_gate)
        {
            var outstanding = _outstanding;
            if (outstanding is null)
                return ChallengeConsumption.Replayed;

            _outstanding = null;
            var elapsed = _clock.GetElapsedTime(outstanding.IssuedTimestamp, _clock.GetTimestamp());
            if (elapsed >= _lifetime)
                return ChallengeConsumption.Expired;

            return ChallengeMatches(outstanding.Value, challenge)
                ? ChallengeConsumption.Accepted
                : ChallengeConsumption.Invalid;
        }
    }

    private static bool ChallengeMatches(string expected, string actual)
    {
        var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expected);
        var actualBytes = System.Text.Encoding.ASCII.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private sealed record OutstandingChallenge(string Value, long IssuedTimestamp);

    private sealed class StopwatchMonotonicClock : IMonotonicClock
    {
        public long GetTimestamp() => Stopwatch.GetTimestamp();

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromSeconds((endingTimestamp - startingTimestamp) / (double)Stopwatch.Frequency);
    }
}
