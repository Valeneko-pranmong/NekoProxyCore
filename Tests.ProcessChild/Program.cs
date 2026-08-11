using System.Security.Cryptography;

namespace Tests.ProcessChild;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int EarlyExitCode = 23;
    private const int PartialReadExitCode = 24;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
            return 64;

        switch (args[0])
        {
            case "READ_TO_EOF":
            case "ECHO_DIAGNOSTIC_SAFE_METADATA_ONLY":
                return await ReadToEofAsync().ConfigureAwait(false);
            case "EARLY_EXIT":
                await Task.Delay(50).ConfigureAwait(false);
                return EarlyExitCode;
            case "FAIL_AFTER_PARTIAL_READ":
                await ReadPartialAsync().ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);
                return PartialReadExitCode;
            case "WRITE_TRANSIENT_PLAINTEXT_FILE":
                return await WriteTransientPlaintextFileAsync().ConfigureAwait(false);
            default:
                return 64;
        }
    }

    private static async Task<int> ReadToEofAsync()
    {
        using var buffer = new MemoryStream();
        await Console.OpenStandardInput().CopyToAsync(buffer).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await Console.Out.WriteLineAsync($"BYTE_COUNT={bytes.Length}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"SHA256={hash}").ConfigureAwait(false);
            await Console.Out.WriteLineAsync("EOF_RECEIVED=YES").ConfigureAwait(false);
            return SuccessExitCode;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task ReadPartialAsync()
    {
        var buffer = new byte[1];
        await Console.OpenStandardInput().ReadAsync(buffer).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(buffer);
    }

    private static async Task<int> WriteTransientPlaintextFileAsync()
    {
        using var buffer = new MemoryStream();
        await Console.OpenStandardInput().CopyToAsync(buffer).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        var path = Path.Combine(
            Path.GetTempPath(),
            $"neko-transient-opaque-config-{Environment.ProcessId}.bin");
        try
        {
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);
            return SuccessExitCode;
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
