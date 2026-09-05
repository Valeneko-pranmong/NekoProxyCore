using System.Collections.Concurrent;
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
        using var observation = fixture.ObservePlaintextActivity();
        var input = CreateSyntheticInput(1024 * 1024);
        var expectedHash = Convert.ToHexString(SHA256.HashData(input));
        var guard = fixture.CreateGuard();
        var processId = 0;
        try
        {
            await guard.StartWithInputAsync("READ_TO_EOF", input);
            processId = guard.Instance.Id;
            Assert.IsTrue(await WaitForExitAsync(guard.Instance, TimeSpan.FromSeconds(10)));
            Assert.AreEqual(0, guard.Instance.ExitCode);

            var diagnostic = await guard.WaitForDiagnosticAsync("EOF_RECEIVED=YES", TimeSpan.FromSeconds(10));
            StringAssert.Contains(diagnostic, $"BYTE_COUNT={input.Length}");
            StringAssert.Contains(diagnostic, $"SHA256={expectedHash}");
            StringAssert.Contains(diagnostic, "EOF_RECEIVED=YES");
            AssertNoSecretMarkers(diagnostic);
            await guard.StopAsync();
            await observation.AssertNoPlaintextActivityAsync();
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
        using var observation = fixture.ObservePlaintextActivity();
        var input = CreateSyntheticInput(64 * 1024 * 1024);
        var guard = fixture.CreateGuard();
        var transfer = guard.StartWithInputAsync("EARLY_EXIT", input);
        var processId = await CaptureOwnedProcessIdAsync(guard.Instance, transfer);
        try
        {
            var exception = await Assert.ThrowsExceptionAsync<IOException>(() => transfer);
            AssertNoSecretMarkers(exception.ToString());
            AssertExactProcessExited(processId);
            AssertNoSecretMarkers(await fixture.ReadGuardLogAsync());
            await observation.AssertNoPlaintextActivityAsync();
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
        using var observation = fixture.ObservePlaintextActivity();
        using var sentinel = fixture.StartSentinel();
        var input = CreateSyntheticInput(64 * 1024 * 1024);
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
            await observation.AssertNoPlaintextActivityAsync();
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
        using var observation = fixture.ObservePlaintextActivity();
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
            await observation.AssertNoPlaintextActivityAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(successInput);
            CryptographicOperations.ZeroMemory(failureInput);
        }
    }

    [TestMethod]
    public async Task FilesystemObservationDetectsTransientDifferentlyNamedPlaintextAsync()
    {
        using var fixture = new Fixture();
        using var observation = fixture.ObservePlaintextActivity();
        var input = CreateSyntheticInput(4096);
        var guard = fixture.CreateGuard();
        try
        {
            await guard.StartWithInputAsync("WRITE_TRANSIENT_PLAINTEXT_FILE", input);
            Assert.IsTrue(await WaitForExitAsync(guard.Instance, TimeSpan.FromSeconds(10)));
            await guard.StopAsync();

            await Assert.ThrowsExceptionAsync<AssertFailedException>(
                () => observation.AssertNoPlaintextActivityAsync());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            await fixture.StopSafelyAsync(guard);
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
        private readonly string _originalCurrentDirectory = Environment.CurrentDirectory;
        private readonly string _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "neko-guard-stdin-test-" + Guid.NewGuid().ToString("N"));
        private readonly string _loggingRoot = Path.Combine(AppContext.BaseDirectory, "logging");
        private readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "logging", "ProcessChild.log");

        public Fixture()
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
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

        public TestGuard CreateGuard() => new(ChildExecutableName, _tempRoot);

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

        public async Task<string> ReadGuardLogAsync(string? requiredFragment = null)
        {
            for (var attempt = 0; attempt < 1000; attempt++)
            {
                if (File.Exists(_logPath))
                {
                    try
                    {
                        var text = await File.ReadAllTextAsync(_logPath);
                        if (requiredFragment == null || text.Contains(requiredFragment, StringComparison.Ordinal))
                            return text;
                    }
                    catch (IOException)
                    {
                    }
                }
                await Task.Delay(10);
            }
            return string.Empty;
        }

        public PlaintextActivityObservation ObservePlaintextActivity() =>
            new(
                new[] { _tempRoot, AppContext.BaseDirectory },
                new[] { _logPath });

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
            Directory.SetCurrentDirectory(_originalCurrentDirectory);
        }
    }

    private sealed class PlaintextActivityObservation : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _violations =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _allowedCreatedFiles;
        private readonly FileSystemWatcher[] _watchers;
        private readonly HashSet<FileSystemWatcher> _strictWatchers = new();

        public PlaintextActivityObservation(
            IEnumerable<string> strictRoots,
            IEnumerable<string> allowedCreatedFiles)
        {
            _allowedCreatedFiles = allowedCreatedFiles
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var watchers = new List<FileSystemWatcher>();
            foreach (var root in strictRoots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var watcher = CreateWatcher(root);
                _strictWatchers.Add(watcher);
                watchers.Add(watcher);
            }

            _watchers = watchers.ToArray();
        }

        public async Task AssertNoPlaintextActivityAsync()
        {
            await Task.Delay(100);
            if (!_violations.IsEmpty)
                Assert.Fail("Plaintext filesystem activity detected: " +
                            string.Join(", ", _violations.Keys.OrderBy(path => path)));
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
                watcher.Dispose();
        }

        private FileSystemWatcher CreateWatcher(string root)
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                Filter = "*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += OnFilesystemActivity;
            watcher.Changed += OnFilesystemActivity;
            watcher.Renamed += OnFilesystemActivity;
            watcher.Error += (_, args) =>
                _violations.TryAdd("WATCHER_ERROR:" + args.GetException().GetType().Name, 0);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void OnFilesystemActivity(object sender, FileSystemEventArgs args)
        {
            var fullPath = Path.GetFullPath(args.FullPath);
            var name = Path.GetFileName(args.FullPath);
            if (Directory.Exists(fullPath))
                return;

            if ((_strictWatchers.Contains((FileSystemWatcher)sender) &&
                 args.ChangeType is (WatcherChangeTypes.Created or WatcherChangeTypes.Renamed) &&
                 !_allowedCreatedFiles.Contains(fullPath)) ||
                string.Equals(name, "settings.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "last.json", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                _violations.TryAdd(fullPath, 0);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        fullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
                    if (SecretMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
                        _violations.TryAdd(fullPath, 0);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
                catch (UnauthorizedAccessException)
                {
                    _violations.TryAdd("UNREADABLE:" + fullPath, 0);
                    return;
                }
            }

            _violations.TryAdd("READ_RETRIES_EXHAUSTED:" + fullPath, 0);
        }
    }

    private sealed class TestGuard : Guard
    {
        private readonly ConcurrentQueue<string> _diagnosticLines = new();
        private readonly SemaphoreSlim _diagnosticChanged = new(0);

        public TestGuard(string childExecutable, string controlledTempRoot) : base(childExecutable)
        {
            Instance.StartInfo.Environment["TMP"] = controlledTempRoot;
            Instance.StartInfo.Environment["TEMP"] = controlledTempRoot;
        }

        public override string Name => "ProcessChild";

        public Task StartWithInputAsync(string mode, ReadOnlyMemory<byte> input) =>
            StartGuardWithStandardInputAsync(mode, input);

        public async Task<string> WaitForDiagnosticAsync(string requiredFragment, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (true)
            {
                var text = string.Join(Environment.NewLine, _diagnosticLines);
                if (text.Contains(requiredFragment, StringComparison.Ordinal))
                    return text;
                await _diagnosticChanged.WaitAsync(cancellation.Token);
            }
        }

        protected override void OnReadNewLine(string line)
        {
            _diagnosticLines.Enqueue(line);
            _diagnosticChanged.Release();
        }
    }
}
