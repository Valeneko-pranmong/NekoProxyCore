using System.Security.Cryptography;
using Netch.Utils;

namespace NekoProxyCore.Legacy;

public sealed record ProtectedSettingsStructuralFacts(
    int ProfileCount,
    int ServerCount,
    bool Pso2ProfileExists,
    bool ProfileServerRelationshipValid);

public static class ProtectedSettingsProvisioner
{
    public static async Task<ProtectedSettingsStructuralFacts> VerifyAsync(
        string protectedPayloadPath,
        string keyPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(protectedPayloadPath) || string.IsNullOrWhiteSpace(keyPath))
            throw new ProtectedSettingsException();

        byte[]? key = null;
        byte[]? plaintext = null;
        try
        {
            key = await File.ReadAllBytesAsync(Path.GetFullPath(keyPath), cancellationToken).ConfigureAwait(false);
            if (key.Length != ProtectedSettingsPayload.KeySizeBytes)
                throw new ProtectedSettingsException();

            await using (var protectedStream = new FileStream(
                             Path.GetFullPath(protectedPayloadPath),
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             4096,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                plaintext = await ProtectedSettingsPayload.OpenAsync(
                        protectedStream,
                        key,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            await using var stream = new MemoryStream(plaintext, writable: false);
            var settings = await Configuration.ParseAsync(stream).ConfigureAwait(false);
            return ValidateStructure(settings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw new ProtectedSettingsException();
        }
        finally
        {
            if (plaintext != null)
                CryptographicOperations.ZeroMemory(plaintext);
            if (key != null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public static async Task<ProtectedSettingsStructuralFacts> ProvisionAsync(
        string externalSettingsPath,
        string protectedPayloadPath,
        string keyPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalSettingsPath) ||
            string.IsNullOrWhiteSpace(protectedPayloadPath) ||
            string.IsNullOrWhiteSpace(keyPath))
            throw new ProtectedSettingsException();

        var input = Path.GetFullPath(externalSettingsPath);
        var payload = Path.GetFullPath(protectedPayloadPath);
        var keyOutput = Path.GetFullPath(keyPath);
        if (string.Equals(input, payload, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, keyOutput, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload, keyOutput, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(payload) ||
            File.Exists(keyOutput))
            throw new ProtectedSettingsException();

        byte[]? key = null;
        try
        {
            await using (var inputStream = new FileStream(
                             input,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             4096,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var settings = await Configuration.ParseAsync(inputStream).ConfigureAwait(false);
                var facts = ValidateStructure(settings);

                key = RandomNumberGenerator.GetBytes(ProtectedSettingsPayload.KeySizeBytes);
                Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
                Directory.CreateDirectory(Path.GetDirectoryName(keyOutput)!);
                inputStream.Position = 0;
                await using (var payloadStream = new FileStream(
                                 payload,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await ProtectedSettingsPayload.SealAsync(
                            inputStream,
                            payloadStream,
                            key,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await using (var keyStream = new FileStream(
                                 keyOutput,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await keyStream.WriteAsync(key, cancellationToken).ConfigureAwait(false);
                    await keyStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                return facts;
            }
        }
        catch (OperationCanceledException)
        {
            DeleteOutputSafely(payload);
            DeleteOutputSafely(keyOutput);
            throw;
        }
        catch
        {
            DeleteOutputSafely(payload);
            DeleteOutputSafely(keyOutput);
            throw new ProtectedSettingsException();
        }
        finally
        {
            if (key != null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    private static ProtectedSettingsStructuralFacts ValidateStructure(Netch.Models.Setting settings)
    {
        var pso2Profiles = settings.Profiles
            .Where(profile => string.Equals(profile.ModeRemark, "PSO2", StringComparison.Ordinal))
            .ToArray();
        var relationshipValid = pso2Profiles.Length == 1 &&
                                settings.Server.Count(server => string.Equals(
                                    server.Remark,
                                    pso2Profiles[0].ServerRemark,
                                    StringComparison.Ordinal)) == 1;
        var facts = new ProtectedSettingsStructuralFacts(
            settings.Profiles.Count,
            settings.Server.Count,
            pso2Profiles.Length == 1,
            relationshipValid);
        if (facts.ProfileCount != 1 || facts.ServerCount != 5 ||
            !facts.Pso2ProfileExists || !facts.ProfileServerRelationshipValid)
            throw new ProtectedSettingsException();
        return facts;
    }

    private static void DeleteOutputSafely(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The original failure remains authoritative and output cleanup is best effort.
        }
    }
}
