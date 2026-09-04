using System.Globalization;
using System.Text;

namespace NekoProxyCore.Core;

/// <summary>A validated, short-lived proxy configuration supplied at launch time.</summary>
public sealed class RuntimeProxyConfig
{
    public const int SupportedSchemaVersion = 1;
    public const long MaxSafeInteger = 9_007_199_254_740_991L;
    public const long LifetimeSeconds = 120L;

    public RuntimeProxyConfig(
        int schemaVersion,
        long configVersion,
        string endpointId,
        string host,
        int port,
        string protocol,
        string cipher,
        SensitiveRuntimeCredential credential,
        long issuedAt,
        long expiresAt)
    {
        if (schemaVersion != SupportedSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (configVersion is < 1 or > MaxSafeInteger)
            throw new ArgumentOutOfRangeException(nameof(configVersion));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (issuedAt is < 0 or > MaxSafeInteger - LifetimeSeconds)
            throw new ArgumentOutOfRangeException(nameof(issuedAt));
        if (expiresAt is < 0 or > MaxSafeInteger)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        if (expiresAt <= issuedAt || expiresAt != issuedAt + LifetimeSeconds)
            throw new ArgumentException("Runtime configuration lifetime is invalid.", nameof(expiresAt));

        SchemaVersion = schemaVersion;
        ConfigVersion = configVersion;
        EndpointId = ValidateAscii(endpointId, 64, nameof(endpointId));
        Host = ValidateAscii(host, 253, nameof(host));
        Port = port;
        Protocol = protocol == "shadowsocks"
            ? protocol
            : throw new ArgumentException("Runtime protocol is unsupported.", nameof(protocol));
        Cipher = ValidateAscii(cipher, 64, nameof(cipher));
        Credential = credential ?? throw new ArgumentNullException(nameof(credential));
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public int SchemaVersion { get; }
    public long ConfigVersion { get; }
    public string EndpointId { get; }
    public string Host { get; }
    public int Port { get; }
    public string Protocol { get; }
    public string Cipher { get; }
    public SensitiveRuntimeCredential Credential { get; }
    public long IssuedAt { get; }
    public long ExpiresAt { get; }

    public byte[] CanonicalBytes()
    {
        var canonical = string.Concat(
            "schema_version=", SchemaVersion.ToString(CultureInfo.InvariantCulture), "\n",
            "config_version=", ConfigVersion.ToString(CultureInfo.InvariantCulture), "\n",
            "endpoint_id=", EndpointId, "\n",
            "host=", Host, "\n",
            "port=", Port.ToString(CultureInfo.InvariantCulture), "\n",
            "protocol=", Protocol, "\n",
            "cipher=", Cipher, "\n",
            "credential=", Credential.RevealForTransport(), "\n",
            "issued_at=", IssuedAt.ToString(CultureInfo.InvariantCulture), "\n",
            "expires_at=", ExpiresAt.ToString(CultureInfo.InvariantCulture), "\n");
        return Encoding.ASCII.GetBytes(canonical);
    }

    public override string ToString() =>
        $"RuntimeProxyConfig(SchemaVersion={SchemaVersion}, ConfigVersion={ConfigVersion}, EndpointId={EndpointId}, Host={Host}, Port={Port}, Protocol={Protocol}, Cipher={Cipher}, Credential=[REDACTED], IssuedAt={IssuedAt}, ExpiresAt={ExpiresAt})";

    internal static string ValidateAscii(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength ||
            value.Any(character => character is < (char)0x20 or > (char)0x7e))
        {
            throw new ArgumentException("Value must be bounded printable ASCII.", parameterName);
        }

        return value;
    }
}

/// <summary>A runtime credential whose ordinary formatting is always redacted.</summary>
public sealed class SensitiveRuntimeCredential
{
    private readonly string _value;

    public SensitiveRuntimeCredential(string value)
    {
        _value = RuntimeProxyConfig.ValidateAscii(value, 256, nameof(value));
    }

    public string RevealForTransport() => _value;

    public override string ToString() => "[REDACTED]";
}
