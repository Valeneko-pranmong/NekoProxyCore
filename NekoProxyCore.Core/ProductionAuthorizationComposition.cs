using System.Security.Cryptography;

namespace NekoProxyCore.Core;

/// <summary>
/// Pins the accepted launch-authorization contract and strict verifier at the production composition boundary.
/// </summary>
public static class ProductionAuthorizationComposition
{
    public const string ProductionKeyId = "neko-prod-key-2";
    public const string ContractId = "NEKO-AUTH-LITE";
    public const string ContractRevision = "lite-v1";
    public const string ContractPackageSha256 = "";

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
        if (publicKeys.Count != 1 || !publicKeys.ContainsKey(ProductionKeyId))
            throw new InvalidOperationException("The Lite production public-key authority is unavailable.");

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
