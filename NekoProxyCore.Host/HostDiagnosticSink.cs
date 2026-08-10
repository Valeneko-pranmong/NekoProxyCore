using NekoProxyCore.Core;

namespace NekoProxyCore.Host;

internal static class HostDiagnosticSink
{
    internal const string EnvironmentVariableName = "NEKO_CORE_DIAGNOSTICS";

    public static ICoreDiagnosticSink Create(TextWriter? errorWriter = null) =>
        IsEnabled(Environment.GetEnvironmentVariable(EnvironmentVariableName))
            ? new SanitizedTextCoreDiagnosticSink(errorWriter ?? Console.Error)
            : NullCoreDiagnosticSink.Instance;

    internal static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal);
}
