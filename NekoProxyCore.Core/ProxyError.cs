using System.Text.RegularExpressions;

namespace NekoProxyCore.Core;

public sealed class ProxyError
{
    public ProxyError(ProxyErrorCode code, string safeMessage)
    {
        Code = code;
        SafeMessage = ErrorSanitizer.Sanitize(safeMessage);
    }

    public ProxyErrorCode Code { get; }

    public string SafeMessage { get; }

    public override string ToString() => $"{Code}: {SafeMessage}";
}

internal static class ErrorSanitizer
{
    private static readonly Regex SecretAssignment = new(
        @"(?<key>password|passwd|token|secret|private[_-]?key|authorization|access[_-]?token)\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BearerToken = new(
        @"\bBearer\s+[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CommandLineSecret = new(
        @"(?:--?|/)(?<key>password|passwd|token|secret|private[_-]?key|authorization|access[_-]?token)\s+[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UriUserInfo = new(
        @"://[^\s/@:]+:[^\s/@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "An unspecified proxy runtime error occurred.";

        var sanitized = SecretAssignment.Replace(message, "${key}=[REDACTED]");
        sanitized = BearerToken.Replace(sanitized, "Bearer [REDACTED]");
        sanitized = CommandLineSecret.Replace(sanitized, "--${key} [REDACTED]");
        sanitized = UriUserInfo.Replace(sanitized, "://[REDACTED]@");
        return sanitized.Length <= 1024 ? sanitized : sanitized[..1024];
    }
}
