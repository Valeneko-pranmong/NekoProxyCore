using System.Security.Cryptography;
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
        LoadModes(canonicalRoot);
    }

    private static void LoadModes(string canonicalRoot)
    {
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

    public static async Task InitializeProtectedAsync(
        string runtimeRoot,
        string protectedSettingsPath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot))
            throw new ArgumentException("A trusted runtime root is required.", nameof(runtimeRoot));
        if (string.IsNullOrWhiteSpace(protectedSettingsPath))
            throw new ProtectedSettingsException();

        var canonicalRoot = Path.GetFullPath(runtimeRoot);
        Directory.SetCurrentDirectory(canonicalRoot);
        AppendPathOnce(Path.Combine(canonicalRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(canonicalRoot, "logging"));

        Global.Settings = new Netch.Models.Setting();
        Global.Modes.Clear();
        byte[]? plaintext = null;
        try
        {
            await using var protectedStream = new FileStream(
                protectedSettingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            plaintext = await ProtectedSettingsPayload.OpenAsync(protectedStream, key, cancellationToken)
                .ConfigureAwait(false);
            await using var plaintextStream = new MemoryStream(plaintext, writable: false);
            Global.Settings = await Configuration.ParseAsync(plaintextStream).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ProtectedSettingsException();
        }
        finally
        {
            if (plaintext != null)
                CryptographicOperations.ZeroMemory(plaintext);
        }

        try
        {
            LoadModes(canonicalRoot);

            var catalog = new NetchProcessModeConfigurationCatalog();
            var result = catalog.GetCatalog();
            if (Global.Settings.Profiles.Count != 1 ||
                Global.Settings.Server.Count != 5 ||
                !result.Succeeded ||
                result.Candidates.Count != 1 ||
                !string.Equals(result.Candidates[0].ProfileReference, "profile-0", StringComparison.Ordinal) ||
                !string.Equals(result.Candidates[0].ServerReference, "server-0", StringComparison.Ordinal) ||
                !catalog.Validate("profile-0", "server-0").Valid)
                throw new ProtectedSettingsException();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Global.Settings = new Netch.Models.Setting();
            Global.Modes.Clear();
            throw new ProtectedSettingsException();
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
