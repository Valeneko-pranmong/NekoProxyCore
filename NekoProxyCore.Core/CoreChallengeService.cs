using System.Security.Cryptography;

namespace NekoProxyCore.Core;

public sealed record CoreChallenge(string Value);

public interface ICoreChallengeService
{
    CoreChallenge Issue();
}

public sealed class CoreChallengeService : ICoreChallengeService
{
    private const int ChallengeByteCount = 32;

    public CoreChallenge Issue()
    {
        var bytes = new byte[ChallengeByteCount];
        RandomNumberGenerator.Fill(bytes);
        var value = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new CoreChallenge(value);
    }
}
