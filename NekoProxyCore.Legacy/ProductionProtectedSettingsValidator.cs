using Netch.Models;
using Netch.Models.Modes;
using Netch.Utils;

namespace NekoProxyCore.Legacy;

public static class ProductionProtectedSettingsValidator
{
    public const string CanonicalProfileReference = "profile-0";
    public const string CanonicalServerReference = "server-0";

    public static ProtectedSettingsStructuralFacts Validate(Setting settings, string trustedModeRoot)
    {
        if (settings == null || string.IsNullOrWhiteSpace(trustedModeRoot))
            throw new ProtectedSettingsException();

        var canonicalModeRoot = Path.GetFullPath(trustedModeRoot);
        var modes = Directory
            .EnumerateFiles(canonicalModeRoot, "*", SearchOption.AllDirectories)
            .Select(path => TryLoadMode(path, canonicalModeRoot))
            .Where(mode => mode != null)
            .Cast<Mode>()
            .ToArray();
        return Validate(settings, modes);
    }

    internal static ProtectedSettingsStructuralFacts Validate(
        Setting settings,
        IEnumerable<Mode> modes)
    {
        if (settings == null || modes == null)
            throw new ProtectedSettingsException();

        try
        {
            var profiles = settings.Profiles.ToArray();
            var servers = settings.Server.ToArray();
            if (profiles.Length != 1 || servers.Length != 5)
                throw new ProtectedSettingsException();

            var profile = profiles[0];
            if (profile.Index != 0 ||
                !string.Equals(profile.ModeRemark, "PSO2", StringComparison.Ordinal))
                throw new ProtectedSettingsException();

            var catalog = new NetchProcessModeConfigurationCatalog(
                new ProcessModeConfigurationSnapshot(settings, modes));
            var result = catalog.GetCatalog();
            var validation = catalog.Validate(
                CanonicalProfileReference,
                CanonicalServerReference);
            if (!result.Succeeded ||
                result.Candidates.Count != 1 ||
                !string.Equals(
                    result.Candidates[0].ProfileReference,
                    CanonicalProfileReference,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    result.Candidates[0].ServerReference,
                    CanonicalServerReference,
                    StringComparison.Ordinal) ||
                !validation.RelationshipValid ||
                validation.ProcessModeMatchCount != 1 ||
                !validation.Valid)
                throw new ProtectedSettingsException();

            return new ProtectedSettingsStructuralFacts(
                profiles.Length,
                servers.Length,
                true,
                true);
        }
        catch (ProtectedSettingsException)
        {
            throw;
        }
        catch
        {
            throw new ProtectedSettingsException();
        }
    }

    private static Mode? TryLoadMode(string path, string modeRoot)
    {
        try
        {
            return ModeHelper.LoadMode(path, modeRoot);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
