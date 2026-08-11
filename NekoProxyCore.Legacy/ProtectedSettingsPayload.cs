using System.Security.Cryptography;
using System.Text;

namespace NekoProxyCore.Legacy;

public sealed class ProtectedSettingsException : Exception
{
    public ProtectedSettingsException() : base("Protected runtime settings are unavailable or invalid.")
    {
    }
}

public static class ProtectedSettingsPayload
{
    public const string DefaultFileName = "runtime-settings.nkps";
    public const int KeySizeBytes = 32;

    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int MaximumPlaintextBytes = 4 * 1024 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("NEKOPS01");
    private const byte FormatVersion = 1;
    private const int HeaderSize = 8 + 1 + NonceSizeBytes + TagSizeBytes;

    public static async Task SealAsync(
        Stream plaintextStream,
        Stream protectedStream,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintextStream);
        ArgumentNullException.ThrowIfNull(protectedStream);
        ValidateKey(key.Span);

        var plaintext = await ReadBoundedAsync(plaintextStream, MaximumPlaintextBytes, cancellationToken)
            .ConfigureAwait(false);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintext.Length];
        var header = new byte[HeaderSize];
        try
        {
            Magic.CopyTo(header, 0);
            header[Magic.Length] = FormatVersion;
            nonce.CopyTo(header, Magic.Length + 1);
            using (var aes = new AesGcm(key.Span))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, header.AsSpan(0, Magic.Length + 1));
            }
            tag.CopyTo(header, Magic.Length + 1 + NonceSizeBytes);
            await protectedStream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await protectedStream.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
            await protectedStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(header);
        }
    }

    public static async Task<byte[]> OpenAsync(
        Stream protectedStream,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedStream);
        ValidateKey(key.Span);

        byte[]? payload = null;
        byte[]? plaintext = null;
        try
        {
            payload = await ReadBoundedAsync(
                    protectedStream,
                    HeaderSize + MaximumPlaintextBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            if (payload.Length <= HeaderSize ||
                !payload.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
                payload[Magic.Length] != FormatVersion)
                throw new ProtectedSettingsException();

            plaintext = Decrypt(payload, key.Span);
            return plaintext;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (plaintext != null)
                CryptographicOperations.ZeroMemory(plaintext);
            throw new ProtectedSettingsException();
        }
        finally
        {
            if (payload != null)
                CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
            throw new ProtectedSettingsException();
    }

    private static byte[] Decrypt(byte[] payload, ReadOnlySpan<byte> key)
    {
        var ciphertext = payload.AsSpan(HeaderSize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key);
            aes.Decrypt(
                payload.AsSpan(Magic.Length + 1, NonceSizeBytes),
                ciphertext,
                payload.AsSpan(Magic.Length + 1 + NonceSizeBytes, TagSizeBytes),
                plaintext,
                payload.AsSpan(0, Magic.Length + 1));
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(block, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return buffer.ToArray();
                if (buffer.Length + read > maximumBytes)
                    throw new ProtectedSettingsException();
                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(block);
            if (buffer.TryGetBuffer(out var segment))
                CryptographicOperations.ZeroMemory(segment.AsSpan(0, checked((int)buffer.Length)));
        }
    }
}
