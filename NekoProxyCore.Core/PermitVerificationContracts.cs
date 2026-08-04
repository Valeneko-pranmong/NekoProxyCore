using System.Security.Cryptography;
using System.Text;

namespace NekoProxyCore.Core;

/// <summary>
/// Bounded opaque authorization material. Its value is intentionally unavailable to logs and public formatting.
/// </summary>
public sealed class SensitivePermit
{
    private readonly string _value;

    private SensitivePermit(string value) => _value = value;

    public static bool TryCreate(string? value, int maximumLength, out SensitivePermit? permit)
    {
        if (maximumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));

        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            permit = null;
            return false;
        }

        permit = new SensitivePermit(value);
        return true;
    }

    public override string ToString() => "[REDACTED]";

    internal string Value => _value;
}

/// <summary>UTC wall clock seam for signed permit time claims; challenge expiry uses a separate monotonic clock.</summary>
public interface IUtcClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Opaque trusted public verification key selected by an approved key resolver.</summary>
public interface ITrustedPublicKey
{
}

public interface ITrustedPublicKeyResolver
{
    bool TryResolve(string keyId, out ITrustedPublicKey? key);
}

/// <summary>Canonicalization seam. Production bytes remain unavailable until the shared S0 fixture is frozen.</summary>
public interface ICanonicalConfigurationSerializer
{
    ReadOnlyMemory<byte> Serialize(ProxyConfiguration configuration);
}

/// <summary>Exact canonical configuration bytes frozen by NEKO-AUTH-S0/s0-rc1.</summary>
public sealed class S0Rc1CanonicalConfigurationSerializer : ICanonicalConfigurationSerializer
{
    private static readonly System.Text.RegularExpressions.Regex ProfileReferencePattern =
        new("^profile-[0-9]{1,6}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex ServerReferencePattern =
        new("^server-[0-9]{1,6}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public ReadOnlyMemory<byte> Serialize(ProxyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Mode != ProxyModeKind.Process ||
            !string.Equals(configuration.ProcessName, "pso2.exe", StringComparison.Ordinal) ||
            configuration.TargetPid is null ||
            !ProfileReferencePattern.IsMatch(configuration.ProfileReference) ||
            !ServerReferencePattern.IsMatch(configuration.ServerReference))
        {
            throw new ArgumentException("A target-bound ProcessMode configuration is required.", nameof(configuration));
        }

        var canonical =
            "protocolVersion=2\n" +
            "mode=ProcessMode\n" +
            "processName=pso2.exe\n" +
            $"targetPid={configuration.TargetPid.Value}\n" +
            $"profileReference={configuration.ProfileReference}\n" +
            $"serverReference={configuration.ServerReference}\n";
        return Encoding.UTF8.GetBytes(canonical);
    }
}

public interface IPermitVerifier
{
    Task<ProxyError?> VerifyAsync(
        SensitivePermit permit,
        ProxyConfiguration configuration,
        string challenge,
        CancellationToken cancellationToken);
}

/// <summary>Raw SHA-256 package identity; textual digest encoding is deliberately not assumed.</summary>
public sealed class ContractFixtureIdentity
{
    private const int Sha256ByteCount = 32;
    private readonly byte[] _packageHash;

    public ContractFixtureIdentity(string revision, ReadOnlySpan<byte> packageHash)
    {
        if (string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("A contract fixture revision is required.", nameof(revision));
        if (packageHash.Length != Sha256ByteCount)
            throw new ArgumentException("A SHA-256 contract fixture hash is required.", nameof(packageHash));

        Revision = revision;
        _packageHash = packageHash.ToArray();
    }

    internal string Revision { get; }

    internal ReadOnlySpan<byte> PackageHash => _packageHash;

    public override string ToString() => "[CONTRACT FIXTURE IDENTITY REDACTED]";
}

public static class ContractFixtureGate
{
    public static void EnsureMatch(ContractFixtureIdentity expected, ContractFixtureIdentity actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (!string.Equals(expected.Revision, actual.Revision, StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(expected.PackageHash, actual.PackageHash))
        {
            throw new ContractFixtureMismatchException();
        }
    }
}

public sealed class ContractFixtureMismatchException : Exception
{
    public ContractFixtureMismatchException()
        : base("Security contract fixture identity does not match.")
    {
    }
}
