using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host;

namespace Tests;

[TestClass]
[DoNotParallelize]
public sealed class LauncherProcessBindingTests
{
    [TestMethod]
    public void CanonicalLauncherPidArgumentIsAccepted()
    {
        Assert.IsTrue(LauncherProcessBinding.TryParseArguments(
            new[] { "--launcher-pid", "4294967295" },
            out var launcherProcessId));
        Assert.AreEqual(uint.MaxValue, launcherProcessId);
    }

    [TestMethod]
    public void CanonicalLauncherPidWithMutableRootArgumentIsAccepted()
    {
        Assert.IsTrue(LauncherProcessBinding.TryParseArguments(
            new[] { "--launcher-pid", "1234", "--mutable-root", @"C:\Temp\Mutable" },
            out var launcherProcessId,
            out var mutableRoot));
        Assert.AreEqual(1234u, launcherProcessId);
        Assert.AreEqual(@"C:\Temp\Mutable", mutableRoot);
    }

    [DataTestMethod]
    [DataRow()]
    [DataRow("--launcher-pid")]
    [DataRow("--launcher-pid", "")]
    [DataRow("--launcher-pid", "0")]
    [DataRow("--launcher-pid", "+1")]
    [DataRow("--launcher-pid", "-1")]
    [DataRow("--launcher-pid", "1.0")]
    [DataRow("--launcher-pid", " 1")]
    [DataRow("--launcher-pid", "1 ")]
    [DataRow("--launcher-pid", "4294967296")]
    [DataRow("--Launcher-Pid", "1")]
    [DataRow("--launcher-pid", "1", "extra")]
    public void MissingOrMalformedLauncherPidArgumentsAreRejected(params string[] arguments)
    {
        Assert.IsFalse(LauncherProcessBinding.TryParseArguments(arguments, out _));
    }

    [TestMethod]
    public void ProductionEntryPointRequiresLauncherPidBeforeHostInitialization()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "NekoProxyCore.Host", "Program.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var parseIndex = source.IndexOf("LauncherProcessBinding.TryParseArguments", StringComparison.Ordinal);
        var leaseIndex = source.IndexOf("SingleInstanceLease.TryAcquire", StringComparison.Ordinal);
        var serverIndex = source.IndexOf("new HeadlessControlServer(", StringComparison.Ordinal);

        Assert.IsTrue(source.Contains("Main(string[] args)", StringComparison.Ordinal));
        Assert.IsTrue(parseIndex >= 0);
        Assert.IsTrue(parseIndex < leaseIndex);
        Assert.IsTrue(serverIndex > parseIndex);
        Assert.IsTrue(source.Contains("configurationCatalog,\n                    launcherProcessId,", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ClientPidLookupFailureDisconnectsBeforeChallengeDispatch()
    {
        var provider = new StubClientProcessIdProvider((false, 0));
        var challenges = new RecordingChallengeService();
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            new InertRuntime(),
            challenges,
            shutdown,
            123,
            UniquePipeName(),
            clientProcessIdProvider: provider);
        var runTask = server.RunAsync(shutdown.Token);

        await AssertRejectedConnectionAsync(server.PipeNameForTesting);

        Assert.AreEqual(1, provider.LookupCount);
        Assert.AreEqual(0, challenges.IssueCount);
        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task MismatchedClientPidDisconnectsBeforeChallengeDispatch()
    {
        var provider = new StubClientProcessIdProvider((true, 124));
        var challenges = new RecordingChallengeService();
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            new InertRuntime(),
            challenges,
            shutdown,
            123,
            UniquePipeName(),
            clientProcessIdProvider: provider);
        var runTask = server.RunAsync(shutdown.Token);

        await AssertRejectedConnectionAsync(server.PipeNameForTesting);

        Assert.AreEqual(1, provider.LookupCount);
        Assert.AreEqual(0, challenges.IssueCount);
        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task EveryAcceptedControlConnectionRequiresAnExactClientPidMatch()
    {
        var provider = new StubClientProcessIdProvider((true, 123), (true, 123));
        var challenges = new RecordingChallengeService();
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            new InertRuntime(),
            challenges,
            shutdown,
            123,
            UniquePipeName(),
            clientProcessIdProvider: provider);
        var runTask = server.RunAsync(shutdown.Token);

        await ExchangeChallengeAsync(server.PipeNameForTesting, "11111111111111111111111111111111");
        await ExchangeChallengeAsync(server.PipeNameForTesting, "22222222222222222222222222222222");

        Assert.AreEqual(2, provider.LookupCount);
        Assert.AreEqual(2, challenges.IssueCount);
        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static string UniquePipeName() => $"NekoProxyCore.Tests.{Guid.NewGuid():N}";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }

    private static async Task AssertRejectedConnectionAsync(string pipeName)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        using var reader = new StreamReader(client, new UTF8Encoding(false, true), false, 1024, true);
        Assert.IsNull(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static async Task ExchangeChallengeAsync(string pipeName, string correlationId)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        var payload = Encoding.UTF8.GetBytes(
            $"{{\"type\":\"challenge\",\"correlationId\":\"{correlationId}\"}}\n");
        await client.WriteAsync(payload);
        await client.FlushAsync();
        using var reader = new StreamReader(client, new UTF8Encoding(false, true), false, 1024, true);
        var response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        StringAssert.Contains(response!, "\"type\":\"challengeResponse\"");
    }

    private sealed class StubClientProcessIdProvider : IControlPipeClientProcessIdProvider
    {
        private readonly Queue<(bool Succeeded, uint ProcessId)> _results;

        public StubClientProcessIdProvider(params (bool Succeeded, uint ProcessId)[] results)
        {
            _results = new Queue<(bool Succeeded, uint ProcessId)>(results);
        }

        public int LookupCount { get; private set; }

        public bool TryGetClientProcessId(NamedPipeServerStream pipe, out uint processId)
        {
            LookupCount++;
            var result = _results.Dequeue();
            processId = result.ProcessId;
            return result.Succeeded;
        }
    }

    private sealed class RecordingChallengeService : ICoreChallengeService
    {
        private readonly CoreChallengeService _inner = new();

        public int IssueCount { get; private set; }

        public CoreChallenge Issue()
        {
            IssueCount++;
            return _inner.Issue();
        }

        public ChallengeConsumption ConsumeForAttempt(string challenge) =>
            _inner.ConsumeForAttempt(challenge);

        public ChallengeAttempt ConsumeOutstandingForAttempt() =>
            _inner.ConsumeOutstandingForAttempt();
    }

    private sealed class InertRuntime : IProxyRuntime
    {
        public Task<ProxyResult> StartAsync(ProxyStartRequest request) =>
            throw new InvalidOperationException("A rejected connection must not dispatch runtime commands.");

        public Task<ProxyResult> StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProxyResult.Success(ProxyStatusKind.Stopped, "runtime"));

        public Task<ProxyStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProxyStatusSnapshot(
                ProxyStatusKind.Stopped,
                "runtime",
                DateTimeOffset.UtcNow));
    }
}
