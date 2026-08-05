using System.Security.Cryptography;
using System.Text.Json;

namespace NekoProxyCore.Host.Authorization;

public static class ProductionPublicKeys
{
    public const string CanonicalKeyId = "neko-prod-key-1";
    public const string ResourceName = "NekoProxyCore.Host.Authorization.production-public-keys.json";

    public static IReadOnlyDictionary<string, RSAParameters> LoadBundled()
    {
        var assembly = typeof(ProductionPublicKeys).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Approved production public keys are unavailable.");
        return Load(stream);
    }

    public static IReadOnlyDictionary<string, RSAParameters> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                HasDuplicateProperties(root) ||
                !HasExactFields(root, "version", "keys") ||
                !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var versionNumber) ||
                versionNumber != 1 ||
                !root.TryGetProperty("keys", out var keysElement) ||
                keysElement.ValueKind != JsonValueKind.Array)
            {
                throw InvalidManifest();
            }

            var keys = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
            foreach (var keyElement in keysElement.EnumerateArray())
            {
                if (keyElement.ValueKind != JsonValueKind.Object ||
                    HasDuplicateProperties(keyElement) ||
                    !HasExactFields(keyElement, "kid", "modulus", "exponent") ||
                    !TryGetBoundedAscii(keyElement, "kid", 128, out var keyId) ||
                    !string.Equals(keyId, CanonicalKeyId, StringComparison.Ordinal) ||
                    !TryGetBase64Url(keyElement, "modulus", out var modulus) ||
                    !TryGetBase64Url(keyElement, "exponent", out var exponent) ||
                    modulus.Length < 256 ||
                    exponent.Length is < 1 or > 8 ||
                    !keys.TryAdd(keyId, new RSAParameters { Modulus = modulus, Exponent = exponent }))
                {
                    throw InvalidManifest();
                }
            }

            if (keys.Count == 0)
                throw InvalidManifest();

            foreach (var parameters in keys.Values)
            {
                using var rsa = RSA.Create();
                rsa.ImportParameters(parameters);
            }

            return keys;
        }
        catch (JsonException exception)
        {
            throw InvalidManifest(exception);
        }
        catch (FormatException exception)
        {
            throw InvalidManifest(exception);
        }
        catch (CryptographicException exception)
        {
            throw InvalidManifest(exception);
        }
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static bool HasExactFields(JsonElement element, params string[] expectedNames)
    {
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        return actual.Length == expected.Count && actual.All(expected.Contains);
    }

    private static bool TryGetBoundedAscii(
        JsonElement element,
        string name,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty) &&
               value.Length <= maximumLength &&
               value.All(character => character <= 0x7f);
    }

    private static bool TryGetBase64Url(JsonElement element, string name, out byte[] value)
    {
        value = Array.Empty<byte>();
        if (!TryGetBoundedAscii(element, name, 2048, out var encoded) ||
            encoded.Contains('=') ||
            encoded.Any(character => !IsBase64UrlCharacter(character)))
        {
            return false;
        }

        var padded = encoded.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        value = Convert.FromBase64String(padded);
        return string.Equals(ToBase64Url(value), encoded, StringComparison.Ordinal);
    }

    private static bool IsBase64UrlCharacter(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '-' or '_';

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static InvalidOperationException InvalidManifest(Exception? innerException = null) =>
        new("The production public-key allow-list is invalid.", innerException);
}
