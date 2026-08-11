using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netch.Controllers;

#pragma warning disable VSTHRD003 // Transfer tasks intentionally start before exact child PID capture.

namespace Tests.Windows;

[TestClass]
[DoNotParallelize]
public sealed class GuardStandardInputProcessTests
{
    private const string ChildExecutableName = "Tests.ProcessChild.exe";
    private static readonly string[] SecretMarkers =
    {
        "SECRET_HOST_MARKER",
        "SECRET_PASSWORD_MARKER",
        "SECRET_SERVER_MARKER"
    };

    [TestMethod]
    public async Task ExactBytesAndEofReachActualOwnedChildWithoutPlaintextPersistenceAsync()
    {
        using var fixture = new Fixture();
        var input = CreateSyntheticInput(1024 * 1024);
        var expectedHash = Convert.ToHexString(SHA256.HashData(input));
        var before = fixture.SnapshotForbiddenPlaintextFiles();
        var guard = fixture.CreateGuard();
        var processId = 0;
        try
        {
            await guard.StartWithInputAsync("READ_TO_EOF", input);
            processId = guard.Instance.Id;
            Assert.IsTrue(await WaitForExitAsync(guard.Instance, TimeSpan.FromSeconds(10)));
            Assert.AreEqual(0, guard.Instance.ExitCode);
            await guard.StopAsync();

            var diagnostic = await fixture.ReadGuardLogAsync();
            StringAssert.Contains(diagnostic, $"BYTE_COUNT={input.Length}");
            StringAssert.Contains(diagnostic, $"SHA256={expectedHash}");
            StringAssert.Contains(diagnostic, "EOF_RECEIVED=YES");
            AssertNoSecretMarkers(diagnostic);
            CollectionAssert.AreEquivalent(
                before,
                fixture.SnapshotForbiddenPlaintextFiles(),
                "The transfer created a forbidden plaintext settings/configuration file.");
            AssertExactProcessExited(processId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            await fixture.StopSafelyAsync(guard);
        }
    }

    [TestMethod]
    public async Task EarlyExitBeforeWriteCompletesFailsClosedAndCleansExactOwnedChildAsync()
    {
        using var fixture = new Fixture();
        var input = CreateSyntheticInput(64 * 1024 * 1024);
        var before = fixture.SnapshotForbiddenPlaintextFiles();
        var guard = fixture.CreateGuard();
        var transfer = guard.StartWithInputAsync("EARLY_EXIT", input);
        var processId = await CaptureOwnedProcessIdAsync(guard.Instance, transfer);
        try
        {
            var exception = await Assert.ThrowsExceptionAsync<IOException>(() => transfer);
            AssertNoSecretMarkers(exception.ToString());
            AssertExactProcessExited(processId);
            AssertNoSecretMarkers(await fixture.ReadGuardLogAsync());
            CollectionAssert.AreEquivalent(
                before,
                fixture.SnapshotForbiddenPlaintextFiles(),
                "The failed transfer created a forbidden plaintext settings/configuration file.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            await fixture.StopSafelyAsync(guard);
        }
    }

    [TestMethod]
    public async Task TransferFailureCleanupStopsOnlyOwnedChildAndLeavesSentinelAliveAsync()
    {
        using var fixture = new Fixture();
        using var sentinel = fixture.StartSentinel();
        var input = CreateSyntheticInput(64 * 1024 * 1024);
        var before = fixture.SnapshotForbiddenPlaintextFiles();
        var guard = fixture.CreateGuard();
        var transfer = guard.StartWithInputAsync("FAIL_AFTER_PARTIAL_READ", input);
        var processId = await CaptureOwnedProcessIdAsync(guard.Instance, transfer);
        try
        {
            var exception = await Assert.ThrowsExceptionAsync<IOException>(() => transfer);
            AssertNoSecretMarkers(exception.ToString());
            AssertExactProcessExited(processId);
            Assert.IsFalse(sentinel.HasExited, "Cleanup terminated a separately owned sentinel process.");
            AssertNoSecretMarkers(await fixture.ReadGuardLogAsync());
            CollectionAssert.AreEquivalent(
                before,
                fixture.SnapshotForbiddenPlaintextFiles(),
                "Transfer-failure cleanup created a forbidden plaintext settings/configuration file.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            await fixture.StopSafelyAsync(guard);
            StopExactProcess(sentinel);
        }
    }

    [TestMethod]
    public async Task SuccessfulAndFailedTransferDiagnosticsNeverDiscloseSecretMarkersAsync()
    {
        using var fixture = new Fixture();
        var successInput = CreateSyntheticInput(4096);
        var failureInput = CreateSyntheticInput(64 * 1024 * 1024);
        try
        {
            var successGuard = fixture.CreateGuard();
            await successGuard.StartWithInputAsync("ECHO_DIAGNOSTIC_SAFE_METADATA_ONLY", successInput);
            Assert.IsTrue(await WaitForExitAsync(successGuard.Instance, TimeSpan.FromSeconds(10)));
            await successGuard.StopAsync();
            AssertNoSecretMarkers(await fixture.ReadGuardLogAsync());

            var failureGuard = fixture.CreateGuard();
            var transfer = failureGuard.StartWithInputAsync("FAIL_AFTER_PARTIAL_READ", failureInput);
            await CaptureOwnedProcessIdAsync(failureGuard.Instance, transfer);
            var exception = await Assert.ThrowsExceptionAsync<IOException>(() => transfer);
            AssertNoSecretMarkers(exception.ToString());
            AssertNoSecretMarkers(await fixture.ReadGuardLogAsync());
            await fixture.StopSafelyAsync(failureGuard);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(successInput);
            CryptographicOperations.ZeroMemory(failureInput);
        }
    }

    private static byte[] CreateSyntheticInput(int minimumLength)
    {
        var marker = Encoding.UTF8.GetBytes(string.Join('|', SecretMarkers) + '|');
        var input = GC.AllocateUninitializedArray<byte>(minimumLength);
        for (var offset = 0; offset < input.Length; offset += marker.Length)
            marker.AsSpan(0, Math.Min(marker.Length, input.Length - offset)).CopyTo(input.AsSpan(offset));
        return input;
    }

    private static async Task<int> CaptureOwnedProcessIdAsync(Process process, Task transfer)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                return process.Id;
            }
            catch (InvalidOperationException)
            {
                if (transfer.IsCompleted)
                    await transfer;
                await Task.Delay(10);
            }
        }

        Assert.Fail("The owned child process did not start within the bounded interval.");
        return 0;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void AssertExactProcessExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.IsTrue(process.HasExited, $"Owned child PID {processId} remained alive.");
        }
        catch (ArgumentException)
        {
            // The exact PID no longer exists, which is the expected no-orphan result.
        }
    }

    private static void AssertNoSecretMarkers(string text)
    {
        foreach (var marker in SecretMarkers)
            Assert.IsFalse(text.Contains(marker, StringComparison.Ordinal), $"Diagnostic marker disclosed: {marker}");
    }

    private static void StopExactProcess(Process process)
    {
        if (process.HasExited)
            return;
        process.Kill(entireProcessTree: false);
        Assert.IsTrue(process.WaitForExit(10000), "The exact test-owned sentinel did not exit.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "neko-guard-stdin-test-" + Guid.NewGuid().ToString("N"));
        private readonly string _loggingRoot = Path.Combine(AppContext.BaseDirectory, "logging");
        private readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "logging", "ProcessChild.log");

        public Fixture()
        {
            Directory.CreateDirectory(_tempRoot);
            Directory.CreateDirectory(Path.Combine(_tempRoot, "data"));
            Directory.CreateDirectory(Path.Combine(_tempRoot, "logging"));
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "bin"));
            Directory.CreateDirectory(_loggingRoot);
            foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "Tests.ProcessChild.*"))
                File.Copy(source, Path.Combine(AppContext.BaseDirectory, "bin", Path.GetFileName(source)), overwrite: true);
            if (File.Exists(_logPath))
                File.Delete(_logPath);
        }

        public TestGuard CreateGuard() => new(ChildExecutableName);

        public Process StartSentinel() => Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "bin", ChildExecutableName),
            Arguments = "READ_TO_EOF",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new AssertFailedException("The test-owned sentinel process did not start.");

        public async Task<string> ReadGuardLogAsync()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (File.Exists(_logPath))
                {
                    try
                    {
                        return await File.ReadAllTextAsync(_logPath);
                    }
                    catch (IOException)
                    {
                    }
                }
                await Task.Delay(10);
            }
            return string.Empty;
        }

        public string[] SnapshotForbiddenPlaintextFiles()
        {
            var roots = new[]
            {
                _tempRoot,
                Path.Combine(_tempRoot, "data"),
                Path.Combine(_tempRoot, "logging"),
                AppContext.BaseDirectory,
                Path.Combine(AppContext.BaseDirectory, "data"),
                _loggingRoot
            };
            return roots
                .Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                .Where(path =>
                    string.Equals(Path.GetFileName(path), "settings.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), "last.json", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public async Task StopSafelyAsync(TestGuard guard)
        {
            try
            {
                await guard.StopAsync();
            }
            catch (InvalidOperationException)
            {
                // Transfer-failure cleanup already disposed the exact owned Process instance.
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
            if (File.Exists(_logPath))
                File.Delete(_logPath);
        }
    }

    private sealed class TestGuard : Guard
    {
        public TestGuard(string childExecutable) : base(childExecutable)
        {
        }

        public override string Name => "ProcessChild";

        public Task StartWithInputAsync(string mode, ReadOnlyMemory<byte> input) =>
            StartGuardWithStandardInputAsync(mode, input);
    }
}
