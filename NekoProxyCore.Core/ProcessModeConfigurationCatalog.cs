using System.Globalization;

namespace NekoProxyCore.Core;

public static class ProcessModeConfigurationCatalogContract
{
    public const int MaximumCandidates = 32;
}

public interface IProcessModeConfigurationCatalog
{
    ProcessModeConfigurationCatalogResult GetCatalog();

    ProcessModeConfigurationValidation Validate(string profileReference, string serverReference);
}

public sealed record ProcessModeConfigurationCandidate(
    string ProfileReference,
    string ServerReference,
    bool RelationshipValid,
    int ProcessModeMatchCount);

public sealed record ProcessModeConfigurationValidation(
    string ProfileReference,
    string ServerReference,
    bool RelationshipValid,
    int ProcessModeMatchCount,
    bool Valid);

public sealed class ProcessModeConfigurationCatalogResult
{
    private ProcessModeConfigurationCatalogResult(
        bool succeeded,
        IReadOnlyList<ProcessModeConfigurationCandidate> candidates,
        ProcessModeConfigurationCatalogFailureReason? failureReason)
    {
        Succeeded = succeeded;
        Candidates = candidates;
        FailureReason = failureReason;
    }

    public bool Succeeded { get; }
    public IReadOnlyList<ProcessModeConfigurationCandidate> Candidates { get; }
    public ProcessModeConfigurationCatalogFailureReason? FailureReason { get; }

    public static ProcessModeConfigurationCatalogResult Success(
        IReadOnlyList<ProcessModeConfigurationCandidate> candidates) =>
        new(
            true,
            candidates?.ToArray() ?? throw new ArgumentNullException(nameof(candidates)),
            null);

    public static ProcessModeConfigurationCatalogResult Failure(
        ProcessModeConfigurationCatalogFailureReason reason)
    {
        if (!Enum.IsDefined(typeof(ProcessModeConfigurationCatalogFailureReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        return new(false, Array.Empty<ProcessModeConfigurationCandidate>(), reason);
    }
}

public enum ProcessModeConfigurationCatalogFailureReason
{
    CatalogUnavailable = 0,
    CatalogTooLarge = 1
}

public static class ProcessModeConfigurationReference
{
    public static bool TryParseProfile(string reference, out int index) =>
        TryParse(reference, "profile-", out index);

    public static bool TryParseServer(string reference, out int index) =>
        TryParse(reference, "server-", out index);

    private static bool TryParse(string reference, string prefix, out int index)
    {
        index = default;
        if (reference == null ||
            !reference.StartsWith(prefix, StringComparison.Ordinal) ||
            reference.Length < prefix.Length + 1 ||
            reference.Length > prefix.Length + 6)
        {
            return false;
        }

        var digits = reference.AsSpan(prefix.Length);
        if (!digits.ToArray().All(character => character is >= '0' and <= '9'))
            return false;

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
