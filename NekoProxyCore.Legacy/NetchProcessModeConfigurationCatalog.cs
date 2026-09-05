using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NekoProxyCore.Core;
using Netch;
using Netch.JsonConverter;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.ProcessMode;

namespace NekoProxyCore.Legacy;

public sealed class NetchProcessModeConfigurationCatalog : IProcessModeConfigurationCatalog
{
    private readonly ProcessModeConfigurationSnapshot? _snapshot;

    public NetchProcessModeConfigurationCatalog()
    {
        try
        {
            _snapshot = new ProcessModeConfigurationSnapshot(
                Global.Settings,
                Global.Modes);
        }
        catch
        {
            _snapshot = null;
        }
    }

    internal NetchProcessModeConfigurationCatalog(ProcessModeConfigurationSnapshot snapshot) =>
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public ProcessModeConfigurationCatalogResult GetCatalog()
    {
        if (_snapshot is null)
        {
            return ProcessModeConfigurationCatalogResult.Failure(
                ProcessModeConfigurationCatalogFailureReason.CatalogUnavailable);
        }

        try
        {
            var candidates = new List<ProcessModeConfigurationCandidate>();
            var profileIndexes = _snapshot.ProfileIndexes
                .Where(index => index is >= 0 and <= 999999)
                .Distinct()
                .OrderBy(index => index);

            foreach (var profileIndex in profileIndexes)
            {
                for (var serverIndex = 0;
                     serverIndex < _snapshot.ServerCount && serverIndex <= 999999;
                     serverIndex++)
                {
                    var profileReference = "profile-" + profileIndex.ToString(CultureInfo.InvariantCulture);
                    var serverReference = "server-" + serverIndex.ToString(CultureInfo.InvariantCulture);
                    var resolution = _snapshot.Evaluate(profileReference, serverReference);
                    if (!resolution.Valid)
                        continue;

                    candidates.Add(new ProcessModeConfigurationCandidate(
                        profileReference,
                        serverReference,
                        true,
                        1));
                    if (candidates.Count > ProcessModeConfigurationCatalogContract.MaximumCandidates)
                    {
                        return ProcessModeConfigurationCatalogResult.Failure(
                            ProcessModeConfigurationCatalogFailureReason.CatalogTooLarge);
                    }
                }
            }

            return ProcessModeConfigurationCatalogResult.Success(candidates);
        }
        catch
        {
            return ProcessModeConfigurationCatalogResult.Failure(
                ProcessModeConfigurationCatalogFailureReason.CatalogUnavailable);
        }
    }

    public ProcessModeConfigurationValidation Validate(
        string profileReference,
        string serverReference)
    {
        if (_snapshot is null)
            throw new InvalidOperationException("The runtime configuration snapshot is unavailable.");

        var resolution = _snapshot.Evaluate(profileReference, serverReference);
        return new ProcessModeConfigurationValidation(
            profileReference,
            serverReference,
            resolution.RelationshipValid,
            resolution.ProcessModeMatchCount,
            resolution.Valid);
    }

    internal ProcessModeConfigurationResolution Resolve(
        string profileReference,
        string serverReference) =>
        _snapshot?.Resolve(profileReference, serverReference) ??
        ProcessModeConfigurationResolution.Invalid(
            ProcessModeConfigurationResolutionFailure.SnapshotUnavailable);
}

internal sealed class ProcessModeConfigurationSnapshot
{
    private readonly ProfileSnapshot[] _profiles;
    private readonly ServerSnapshot[] _servers;
    private readonly RedirectorSnapshot[] _modes;
    private readonly Setting _runtimeSettings;

    public ProcessModeConfigurationSnapshot(
        Setting settings,
        IEnumerable<Mode> modes)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _runtimeSettings = CloneSettings(settings);
        _profiles = settings.Profiles
            .Select(profile => new ProfileSnapshot(
                profile.Index,
                profile.ServerRemark,
                profile.ModeRemark))
            .ToArray();
        _servers = settings.Server
            .Select(CloneServer)
            .Select(server => new ServerSnapshot(server, server.Remark))
            .ToArray();
        _modes = modes?
            .OfType<Redirector>()
            .Select(CloneRedirector)
            .Select(mode => new RedirectorSnapshot(mode, mode.Remark.Values.ToArray()))
            .ToArray() ?? throw new ArgumentNullException(nameof(modes));
    }

    public IEnumerable<int> ProfileIndexes => _profiles.Select(profile => profile.Index);
    public int ServerCount => _servers.Length;

    public ProcessModeConfigurationResolution Evaluate(
        string profileReference,
        string serverReference)
    {
        if (!ProcessModeConfigurationReference.TryParseProfile(profileReference, out var profileIndex))
            return ProcessModeConfigurationResolution.Invalid(ProcessModeConfigurationResolutionFailure.ProfileReferenceInvalid);
        if (!ProcessModeConfigurationReference.TryParseServer(serverReference, out var serverIndex))
            return ProcessModeConfigurationResolution.Invalid(ProcessModeConfigurationResolutionFailure.ServerReferenceInvalid);

        var matchingProfiles = _profiles.Where(profile => profile.Index == profileIndex).Take(2).ToArray();
        if (matchingProfiles.Length != 1)
        {
            return ProcessModeConfigurationResolution.Invalid(
                matchingProfiles.Length == 0
                    ? ProcessModeConfigurationResolutionFailure.ProfileNotFound
                    : ProcessModeConfigurationResolutionFailure.ProfileAmbiguous);
        }

        var profile = matchingProfiles[0];
        var matchingModes = _modes
            .Where(mode => mode.Remarks.Any(value =>
                string.Equals(value, profile.ModeRemark, StringComparison.Ordinal)))
            .Take(2)
            .ToArray();
        var matchCount = Math.Min(matchingModes.Length, 2);

        if (serverIndex < 0 || serverIndex >= _servers.Length)
        {
            return new ProcessModeConfigurationResolution(
                false,
                matchCount,
                false,
                ProcessModeConfigurationResolutionFailure.ServerNotFound,
                null,
                null,
                null);
        }

        var server = _servers[serverIndex];
        var relationshipValid = string.Equals(
            profile.ServerRemark,
            server.Remark,
            StringComparison.Ordinal);

        if (!relationshipValid)
        {
            return new ProcessModeConfigurationResolution(
                false,
                matchCount,
                false,
                ProcessModeConfigurationResolutionFailure.ProfileServerMismatch,
                null,
                null,
                null);
        }

        if (matchCount != 1)
        {
            return new ProcessModeConfigurationResolution(
                true,
                matchCount,
                false,
                matchCount == 0
                    ? ProcessModeConfigurationResolutionFailure.ModeNotFound
                    : ProcessModeConfigurationResolutionFailure.ModeAmbiguous,
                null,
                null,
                null);
        }

        return new ProcessModeConfigurationResolution(
            true,
            1,
            true,
            ProcessModeConfigurationResolutionFailure.None,
            server.Server,
            matchingModes[0].Mode,
            null);
    }

    public ProcessModeConfigurationResolution Resolve(
        string profileReference,
        string serverReference)
    {
        var resolution = Evaluate(profileReference, serverReference);
        if (!resolution.Valid)
            return resolution;

        return resolution with
        {
            Server = CloneServer(resolution.Server!),
            Mode = CloneRedirector(resolution.Mode!),
            RuntimeSettings = CloneSettings(_runtimeSettings)
        };
    }

    private sealed record ProfileSnapshot(
        int Index,
        string ServerRemark,
        string ModeRemark);

    private sealed record ServerSnapshot(Server Server, string Remark);

    private sealed record RedirectorSnapshot(
        Redirector Mode,
        IReadOnlyList<string> Remarks);

    private static Server CloneServer(Server server)
    {
        var type = server.GetType();
        return (Server)(JsonSerializer.Deserialize(
            JsonSerializer.Serialize(server, type, Global.NewCustomJsonSerializerOptions()),
            type,
            Global.NewCustomJsonSerializerOptions()) ??
            throw new InvalidOperationException("The runtime server snapshot could not be created."));
    }

    private static Redirector CloneRedirector(Redirector mode)
    {
        var clone = JsonSerializer.Deserialize<Redirector>(
                        JsonSerializer.Serialize(mode, Global.NewCustomJsonSerializerOptions()),
                        Global.NewCustomJsonSerializerOptions()) ??
                    throw new InvalidOperationException("The runtime mode snapshot could not be created.");
        clone.FullName = mode.FullName;
        return clone;
    }

    private static Setting CloneSettings(Setting settings)
    {
        var options = Global.NewCustomJsonSerializerOptions();
        options.Converters.Add(new ServerConverterWithTypeDiscriminator());
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<Setting>(
                   JsonSerializer.Serialize(settings, options),
                   options) is { } clone
            ? RestoreIgnoredSettings(settings, clone)
            : throw new InvalidOperationException("The runtime settings snapshot could not be created.");
    }

    private static Setting RestoreIgnoredSettings(Setting source, Setting clone)
    {
        clone.AioDNS.ListenPort = source.AioDNS.ListenPort;
        return clone;
    }
}

internal sealed record ProcessModeConfigurationResolution(
    bool RelationshipValid,
    int ProcessModeMatchCount,
    bool Valid,
    ProcessModeConfigurationResolutionFailure Failure,
    Server? Server,
    Redirector? Mode,
    Setting? RuntimeSettings)
{
    public static ProcessModeConfigurationResolution Invalid(
        ProcessModeConfigurationResolutionFailure failure) =>
        new(false, 0, false, failure, null, null, null);
}

internal enum ProcessModeConfigurationResolutionFailure
{
    None,
    ProfileReferenceInvalid,
    ServerReferenceInvalid,
    ProfileNotFound,
    ProfileAmbiguous,
    ServerNotFound,
    ProfileServerMismatch,
    ModeNotFound,
    ModeAmbiguous,
    SnapshotUnavailable
}
