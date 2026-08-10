using System.Security.Cryptography;

namespace NekoProxyCore.Core;

/// <summary>
/// Pins the accepted launch-authorization contract and strict verifier at the production composition boundary.
/// </summary>
public static class ProductionAuthorizationComposition
{
    public const string ContractId = "NEKO-AUTH-S0";
    public const string ContractRevision = "s0-rc1";
    public const string ContractPackageSha256 =
        "6697351b6b280afc566fedaaa1a6cfe207b1ea1d803c2eb613b4c1a891e192df";

    public static IProxyStartAuthorizer CreateStartAuthorizer(
        IReadOnlyDictionary<string, RSAParameters> publicKeys,
        ITrustedUtcClock? clock = null) =>
        CreateStartAuthorizer(publicKeys, clock, NullCoreDiagnosticSink.Instance);

    public static IProxyStartAuthorizer CreateStartAuthorizer(
        IReadOnlyDictionary<string, RSAParameters> publicKeys,
        ITrustedUtcClock? clock,
        ICoreDiagnosticSink? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);
        if (publicKeys.Count == 0)
            throw new InvalidOperationException("Approved production public keys are unavailable.");

        var trustedKeys = new Dictionary<string, ITrustedPublicKey>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in publicKeys)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) ||
                    entry.Key.Length > 128 ||
                    entry.Key.Any(character => character > 0x7f) ||
                    trustedKeys.ContainsKey(entry.Key))
                {
                    throw new InvalidOperationException("The production public-key allow-list is invalid.");
                }

                trustedKeys.Add(entry.Key, RsaTrustedPublicKey.FromParameters(entry.Value));
            }

            var verifier = new StrictLaunchPermitVerifier(
                new ImmutableTrustedPublicKeyResolver(trustedKeys),
                new S0Rc1CanonicalConfigurationSerializer(),
                clock ?? SystemTrustedUtcClock.Instance,
                new InMemoryPermitReplayStore(),
                diagnostics);
            return new ChallengePermitStartAuthorizer(verifier, diagnostics);
        }
        catch
        {
            foreach (var key in trustedKeys.Values.OfType<IDisposable>())
                key.Dispose();
            throw;
        }
    }

    private sealed class SystemTrustedUtcClock : ITrustedUtcClock
    {
        public static readonly SystemTrustedUtcClock Instance = new();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public bool IsTrusted => true;
    }
}
