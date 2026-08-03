using Netch;
using Netch.Utils;

namespace NekoProxyCore.Legacy;

/// <summary>
/// Initializes the legacy runtime state required by ProcessMode without entering a UI path.
/// The runtime root must come from the trusted host composition boundary.
/// </summary>
public static class NetchRuntimeBootstrap
{
    public static async Task InitializeAsync(string runtimeRoot)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot))
            throw new ArgumentException("A trusted runtime root is required.", nameof(runtimeRoot));

        var canonicalRoot = Path.GetFullPath(runtimeRoot);
        Directory.SetCurrentDirectory(canonicalRoot);
        AppendPathOnce(Path.Combine(canonicalRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(canonicalRoot, "logging"));

        await Configuration.LoadAsync().ConfigureAwait(false);
        Global.Modes.Clear();
        var modeRoot = Path.Combine(canonicalRoot, "mode");
        foreach (var file in Directory.EnumerateFiles(modeRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                Global.Modes.Add(ModeHelper.LoadMode(file));
            }
            catch (NotSupportedException)
            {
                // Unrelated mode files may coexist in the trusted runtime. Session resolution
                // remains authoritative and returns a typed error when no unique mode matches.
            }
        }
    }

    private static void AppendPathOnce(string directory)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var entries = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            try
            {
                if (string.Equals(
                        Path.GetFullPath(entry.Trim().Trim('"')),
                        directory,
                        StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Ignore malformed inherited PATH entries; the trusted runtime bin is appended below.
            }
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            string.IsNullOrEmpty(current) ? directory : $"{current}{Path.PathSeparator}{directory}");
    }
}
