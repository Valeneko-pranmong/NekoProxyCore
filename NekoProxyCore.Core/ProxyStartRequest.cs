namespace NekoProxyCore.Core;

public sealed class ProxyStartRequest
{
    public ProxyStartRequest(
        ProxyConfiguration configuration,
        string? correlationId = null,
        CancellationToken cancellationToken = default,
        SensitivePermit? permit = null,
        string? admittedChallenge = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        CorrelationId = ValidateCorrelationId(correlationId);
        CancellationToken = cancellationToken;
        Permit = permit;
        AdmittedChallenge = admittedChallenge;
    }

    public ProxyConfiguration Configuration { get; }

    /// <summary>Opaque diagnostic id; callers must never put credentials in it.</summary>
    public string CorrelationId { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>Opaque server-issued launch authorization; never render or persist this value.</summary>
    public SensitivePermit? Permit { get; }

    /// <summary>Challenge atomically consumed when the bounded start frame was admitted.</summary>
    public string? AdmittedChallenge { get; }

    private static string ValidateCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Guid.NewGuid().ToString("N");

        var trimmed = value.Trim();
        if (trimmed.Length > 128 || trimmed.Any(c => !char.IsLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Correlation id is invalid.", nameof(value));

        return trimmed;
    }
}
