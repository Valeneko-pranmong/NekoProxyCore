namespace NekoProxyCore.Core;

/// <summary>
/// Sanitized, immutable input required by a runtime mode.
/// Credentials and other secret material intentionally do not belong here.
/// </summary>
public sealed class ProxyConfiguration
{
    public ProxyConfiguration(
        ProxyModeKind mode,
        string processName,
        string profileReference,
        string serverReference,
        TimeSpan? startTimeout = null,
        TimeSpan? stopTimeout = null)
    {
        Mode = mode;
        ProcessName = ValidateIdentifier(processName, nameof(processName));
        ProfileReference = ValidateIdentifier(profileReference, nameof(profileReference));
        ServerReference = ValidateIdentifier(serverReference, nameof(serverReference));
        StartTimeout = ValidateTimeout(startTimeout ?? TimeSpan.FromSeconds(30), nameof(startTimeout));
        StopTimeout = ValidateTimeout(stopTimeout ?? TimeSpan.FromSeconds(15), nameof(stopTimeout));
    }

    public ProxyModeKind Mode { get; }

    /// <summary>Executable name used by ProcessMode discovery (for example, pso2.exe).</summary>
    public string ProcessName { get; }

    /// <summary>Opaque profile identifier. It is not a path containing secrets.</summary>
    public string ProfileReference { get; }

    /// <summary>Opaque server identifier resolved by the host, never a password-bearing URI.</summary>
    public string ServerReference { get; }

    public TimeSpan StartTimeout { get; }

    public TimeSpan StopTimeout { get; }

    /// <summary>
    /// Creates a validated configuration without allowing validation details to cross the runtime boundary.
    /// </summary>
    public static bool TryCreate(
        ProxyModeKind mode,
        string processName,
        string profileReference,
        string serverReference,
        TimeSpan? startTimeout,
        TimeSpan? stopTimeout,
        out ProxyConfiguration? configuration,
        out ProxyError? error)
    {
        try
        {
            configuration = new ProxyConfiguration(mode, processName, profileReference, serverReference, startTimeout, stopTimeout);
            error = null;
            return true;
        }
        catch (ArgumentException)
        {
            configuration = null;
            error = new ProxyError(ProxyErrorCode.InvalidConfiguration, "Proxy configuration is invalid.");
            return false;
        }
    }

    private static string ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty identifier is required.", parameterName);

        var trimmed = value.Trim();
        if (trimmed.Length > 256 || trimmed.Any(c => !char.IsLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Identifier is invalid.", parameterName);

        return trimmed;
    }

    private static TimeSpan ValidateTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(parameterName, "Timeout must be greater than zero and no longer than ten minutes.");

        return value;
    }
}
