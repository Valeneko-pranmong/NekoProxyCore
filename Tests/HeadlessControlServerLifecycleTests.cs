using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host;
using NekoProxyCore.Host.Protocol;

namespace Tests;

[TestClass]
[DoNotParallelize]
public sealed class HeadlessControlServerLifecycleTests
{
    [TestMethod]
    public async Task StoppedShutdownAcknowledgesBeforeHostCancellationCompletes()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Stopped);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        var response = await ExchangeAsync(server.PipeNameForTesting, ShutdownRequest);

        AssertShutdownSucceeded(response);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(shutdown.IsShutdownRequested);
        Assert.AreEqual(1, runtime.StopCount);
    }

    [TestMethod]
    public async Task RunningShutdownStopsRuntimeBeforeAcknowledgingAndCancellingHost()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Running);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        var response = await ExchangeAsync(server.PipeNameForTesting, ShutdownRequest);

        AssertShutdownSucceeded(response);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, runtime.StopCount);
        Assert.IsTrue(shutdown.IsShutdownRequested);
    }

    [TestMethod]
    public async Task StopLeavesHostAvailableForStatus()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Running);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        await using var client = await ConnectAsync(server.PipeNameForTesting);
        var stopResponse = await ExchangeAsync(client,
            "{\"type\":\"stop\",\"correlationId\":\"11111111111111111111111111111111\"}");
        var statusResponse = await ExchangeAsync(client,
            "{\"type\":\"status\",\"correlationId\":\"22222222222222222222222222222222\"}");

        StringAssert.Contains(stopResponse, "\"type\":\"stopResponse\"");
        StringAssert.Contains(statusResponse, "\"type\":\"statusResponse\"");
        Assert.IsFalse(shutdown.IsShutdownRequested);
        Assert.IsFalse(runTask.IsCompleted);

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task RuntimeConfigurationCommandsAreReadOnlyAndLeaveStoppedRuntimeUntouched()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Stopped);
        var catalog = new RecordingCatalog();
        var challenges = new RecordingChallengeService();
        var seededChallenge = challenges.SeedOutstanding();
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            challenges,
            shutdown,
            catalog,
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        await using var client = await ConnectAsync(server.PipeNameForTesting);
        var catalogResponse = await ExchangeAsync(client,
            "{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"11111111111111111111111111111111\"}");
        var validationResponse = await ExchangeAsync(client,
            "{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"22222222222222222222222222222222\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}");

        StringAssert.Contains(catalogResponse, "\"type\":\"runtimeConfigCatalogResponse\"");
        StringAssert.Contains(validationResponse, "\"type\":\"runtimeConfigValidateResponse\"");
        Assert.AreEqual(1, catalog.CatalogCount);
        Assert.AreEqual(1, catalog.ValidationCount);
        Assert.AreEqual(0, runtime.StartCount);
        Assert.AreEqual(0, runtime.StopCount);
        Assert.AreEqual(0, runtime.StatusCount);
        Assert.AreEqual(0, challenges.IssueCount);
        Assert.AreEqual(0, challenges.ConsumeCount);
        Assert.AreEqual(0, challenges.ConsumeOutstandingCount);
        Assert.AreEqual(
            ChallengeConsumption.Accepted,
            challenges.ConsumeSeeded(seededChallenge.Value));

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task OversizedRealFrameIsDroppedAndServerRemainsAvailable()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Stopped);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        await using (var exactClient = await ConnectAsync(server.PipeNameForTesting))
        {
            var exactRequest = CreateFrameWithExactByteCount(ControlProtocol.MaxFrameBytes);
            var response = await ExchangeAsync(exactClient, exactRequest);
            StringAssert.Contains(response, "\"errorCode\":\"ProtocolInvalid\"");
        }

        await using (var oversizedClient = await ConnectAsync(server.PipeNameForTesting))
        {
            try
            {
                var oversizedRequest = CreateFrameWithExactByteCount(ControlProtocol.MaxFrameBytes + 1);
                var payload = Encoding.UTF8.GetBytes(oversizedRequest + "\n");
                await oversizedClient.WriteAsync(payload);
                await oversizedClient.FlushAsync();
                using var reader = new StreamReader(
                    oversizedClient,
                    new UTF8Encoding(false, true),
                    false,
                    1024,
                    true);
                Assert.IsNull(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            }
            catch (IOException)
            {
                // Closing an oversized client frame may surface as EOF or a broken pipe.
            }
        }

        var statusResponse = await ExchangeAsync(
            server.PipeNameForTesting,
            "{\"type\":\"status\",\"correlationId\":\"33333333333333333333333333333333\"}");
        StringAssert.Contains(statusResponse, "\"type\":\"statusResponse\"");
        Assert.IsFalse(runTask.IsCompleted);

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    public async Task RealDispatchPreservesCatalogBoundariesWithoutTruncation(int candidateCount)
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Stopped);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            new BoundaryCatalog(candidateCount),
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        var response = await ExchangeAsync(
            server.PipeNameForTesting,
            "{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"11111111111111111111111111111111\"}");

        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        Assert.AreEqual("runtimeConfigCatalogResponse", root.GetProperty("type").GetString());
        if (candidateCount > ProcessModeConfigurationCatalogContract.MaximumCandidates)
        {
            Assert.IsFalse(root.GetProperty("succeeded").GetBoolean());
            Assert.AreEqual("CatalogTooLarge", root.GetProperty("reason").GetString());
            Assert.IsFalse(root.TryGetProperty("candidates", out _));
        }
        else
        {
            Assert.IsTrue(root.GetProperty("succeeded").GetBoolean());
            var candidates = root.GetProperty("candidates");
            Assert.AreEqual(candidateCount, candidates.GetArrayLength());
            for (var index = 0; index < candidateCount; index++)
            {
                var candidate = candidates[index];
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        "profileReference",
                        "serverReference",
                        "relationshipValid",
                        "processModeMatchCount"
                    },
                    candidate.EnumerateObject().Select(property => property.Name).ToArray());
                Assert.AreEqual($"profile-{index}", candidate.GetProperty("profileReference").GetString());
                Assert.AreEqual("server-0", candidate.GetProperty("serverReference").GetString());
                Assert.IsTrue(candidate.GetProperty("relationshipValid").GetBoolean());
                Assert.AreEqual(1, candidate.GetProperty("processModeMatchCount").GetInt32());
            }
        }
        Assert.AreEqual(0, runtime.StartCount);

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [DataTestMethod]
    [DataRow(true, 1, true, true)]
    [DataRow(false, 0, false, true)]
    [DataRow(false, 1, false, true)]
    [DataRow(true, 0, false, true)]
    [DataRow(true, 2, false, true)]
    [DataRow(true, 1, false, false)]
    [DataRow(false, 1, true, false)]
    [DataRow(true, 0, true, false)]
    [DataRow(true, 2, true, false)]
    public async Task RealDispatchContainsValidationTruthTable(
        bool relationshipValid,
        int processModeMatchCount,
        bool valid,
        bool providerFactsAreConsistent)
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Stopped);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            new ValidationCatalog(relationshipValid, processModeMatchCount, valid),
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        var response = await ExchangeAsync(
            server.PipeNameForTesting,
            "{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"22222222222222222222222222222222\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}");

        if (providerFactsAreConsistent)
        {
            Assert.AreEqual(
                $"{{\"type\":\"runtimeConfigValidateResponse\",\"correlationId\":\"22222222222222222222222222222222\",\"succeeded\":true,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"relationshipValid\":{relationshipValid.ToString().ToLowerInvariant()},\"processModeMatchCount\":{processModeMatchCount},\"valid\":{valid.ToString().ToLowerInvariant()}}}",
                response);
        }
        else
        {
            Assert.AreEqual(
                "{\"type\":\"runtimeConfigValidateResponse\",\"correlationId\":\"22222222222222222222222222222222\",\"succeeded\":false,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"relationshipValid\":false,\"processModeMatchCount\":0,\"valid\":false}",
                response);
        }
        Assert.AreEqual(0, runtime.StartCount);

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task RuntimeConfigurationFailuresUseSafeFixedResponsesWithoutExceptionText()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Stopped);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            new ThrowingCatalog(),
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        await using var client = await ConnectAsync(server.PipeNameForTesting);
        var catalogResponse = await ExchangeAsync(client,
            "{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"11111111111111111111111111111111\"}");
        var validationResponse = await ExchangeAsync(client,
            "{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"22222222222222222222222222222222\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}");

        Assert.AreEqual(
            "{\"type\":\"runtimeConfigCatalogResponse\",\"correlationId\":\"11111111111111111111111111111111\",\"succeeded\":false,\"reason\":\"CatalogUnavailable\"}",
            catalogResponse);
        Assert.AreEqual(
            "{\"type\":\"runtimeConfigValidateResponse\",\"correlationId\":\"22222222222222222222222222222222\",\"succeeded\":false,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"relationshipValid\":false,\"processModeMatchCount\":0,\"valid\":false}",
            validationResponse);
        Assert.IsFalse((catalogResponse + validationResponse).Contains(
            "SECRET_EXCEPTION_TEXT",
            StringComparison.Ordinal));
        Assert.AreEqual(0, runtime.StartCount);
        Assert.AreEqual(0, runtime.StopCount);
        Assert.AreEqual(0, runtime.StatusCount);

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task InvalidRuntimeConfigurationProviderResultsFailClosedAndKeepServerAvailable()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Stopped);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            new InvalidResultCatalog(),
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        await using var client = await ConnectAsync(server.PipeNameForTesting);
        var catalogResponse = await ExchangeAsync(client,
            "{\"type\":\"runtimeConfigCatalog\",\"correlationId\":\"11111111111111111111111111111111\"}");
        var validationResponse = await ExchangeAsync(client,
            "{\"type\":\"runtimeConfigValidate\",\"correlationId\":\"22222222222222222222222222222222\",\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\"}");
        var statusResponse = await ExchangeAsync(client,
            "{\"type\":\"status\",\"correlationId\":\"33333333333333333333333333333333\"}");

        Assert.AreEqual(
            "{\"type\":\"runtimeConfigCatalogResponse\",\"correlationId\":\"11111111111111111111111111111111\",\"succeeded\":false,\"reason\":\"CatalogUnavailable\"}",
            catalogResponse);
        Assert.AreEqual(
            "{\"type\":\"runtimeConfigValidateResponse\",\"correlationId\":\"22222222222222222222222222222222\",\"succeeded\":false,\"profileReference\":\"profile-0\",\"serverReference\":\"server-0\",\"relationshipValid\":false,\"processModeMatchCount\":0,\"valid\":false}",
            validationResponse);
        StringAssert.Contains(statusResponse, "\"type\":\"statusResponse\"");
        Assert.AreEqual(0, runtime.StartCount);
        Assert.AreEqual(0, runtime.StopCount);
        Assert.AreEqual(1, runtime.StatusCount);

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task FailedRuntimeStopDoesNotAcknowledgeOrCancelHost()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Running, stopSucceeds: false);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            unchecked((uint)Environment.ProcessId),
            UniquePipeName());
        var runTask = server.RunAsync(shutdown.Token);

        var response = await ExchangeAsync(server.PipeNameForTesting, ShutdownRequest);

        StringAssert.Contains(response, "\"type\":\"shutdownResponse\"");
        StringAssert.Contains(response, "\"succeeded\":false");
        StringAssert.Contains(response, "\"errorCode\":\"StopFailed\"");
        Assert.IsFalse(shutdown.IsShutdownRequested);
        Assert.IsFalse(runTask.IsCompleted);

        shutdown.RequestShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task SingleInstanceLeaseCanBeDisposedByHostContinuationAndReacquired()
    {
        var mutexName = $"Local\\NekoProxyCore.Tests.{Guid.NewGuid():N}";
        var acquired = new TaskCompletionSource<SingleInstanceLease>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitionThread = new Thread(() =>
        {
            if (!SingleInstanceLease.TryAcquire(out var lease, mutexName))
                acquired.SetException(new InvalidOperationException("Initial lease acquisition failed."));
            else
                acquired.SetResult(lease!);
        });
        acquisitionThread.Start();

        var first = await acquired.Task.WaitAsync(TimeSpan.FromSeconds(2));
        acquisitionThread.Join();
        Assert.IsFalse(SingleInstanceLease.TryAcquire(out var blocked, mutexName));
        Assert.IsNull(blocked);

        first.Dispose();

        Assert.IsTrue(SingleInstanceLease.TryAcquire(out var reacquired, mutexName));
        reacquired!.Dispose();
    }

    private const string ShutdownRequest =
        "{\"type\":\"shutdown\",\"correlationId\":\"0123456789abcdef0123456789abcdef\"}";

    private static string UniquePipeName() => $"NekoProxyCore.Tests.{Guid.NewGuid():N}";

    private static string CreateFrameWithExactByteCount(int byteCount) =>
        new('x', byteCount);

    private static async Task<string> ExchangeAsync(string pipeName, string request)
    {
        await using var client = await ConnectAsync(pipeName);
        return await ExchangeAsync(client, request);
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName)
    {
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        return client;
    }

    private static async Task<string> ExchangeAsync(Stream stream, string request)
    {
        var payload = Encoding.UTF8.GetBytes(request + "\n");
        await stream.WriteAsync(payload);
        await stream.FlushAsync();

        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 1024, true);
        return await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2))
            ?? throw new IOException("Control server closed before responding.");
    }

    private static void AssertShutdownSucceeded(string response)
    {
        using var document = JsonDocument.Parse(response);
        Assert.AreEqual("shutdownResponse", document.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("0123456789abcdef0123456789abcdef", document.RootElement.GetProperty("correlationId").GetString());
        Assert.IsTrue(document.RootElement.GetProperty("succeeded").GetBoolean());
    }

    private sealed class RecordingRuntime : IProxyRuntime
    {
        private ProxyStatusKind _status;
        private readonly bool _stopSucceeds;

        public RecordingRuntime(ProxyStatusKind status, bool stopSucceeds = true)
        {
            _status = status;
            _stopSucceeds = stopSucceeds;
        }

        public int StopCount { get; private set; }
        public int StartCount { get; private set; }
        public int StatusCount { get; private set; }

        public Task<ProxyResult> StartAsync(ProxyStartRequest request)
        {
            StartCount++;
            return Task.FromResult(ProxyResult.Failure(
                _status,
                request.CorrelationId,
                new ProxyError(ProxyErrorCode.AuthorizationRequired, "Authorization is required.")));
        }

        public Task<ProxyResult> StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            if (!_stopSucceeds)
            {
                _status = ProxyStatusKind.Failed;
                return Task.FromResult(ProxyResult.Failure(
                    _status,
                    "runtime",
                    new ProxyError(ProxyErrorCode.StopFailed, "Proxy stop failed.")));
            }

            _status = ProxyStatusKind.Stopped;
            return Task.FromResult(ProxyResult.Success(_status, "runtime"));
        }

        public Task<ProxyStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusCount++;
            return Task.FromResult(new ProxyStatusSnapshot(_status, "runtime", DateTimeOffset.UtcNow));
        }
    }

    private sealed class RecordingCatalog : IProcessModeConfigurationCatalog
    {
        public int CatalogCount { get; private set; }
        public int ValidationCount { get; private set; }

        public ProcessModeConfigurationCatalogResult GetCatalog()
        {
            CatalogCount++;
            return ProcessModeConfigurationCatalogResult.Success(new[]
            {
                new ProcessModeConfigurationCandidate("profile-0", "server-0", true, 1)
            });
        }

        public ProcessModeConfigurationValidation Validate(
            string profileReference,
            string serverReference)
        {
            ValidationCount++;
            return new ProcessModeConfigurationValidation(
                profileReference,
                serverReference,
                true,
                1,
                true);
        }
    }

    private sealed class RecordingChallengeService : ICoreChallengeService
    {
        private readonly CoreChallengeService _inner = new();

        public int IssueCount { get; private set; }
        public int ConsumeCount { get; private set; }
        public int ConsumeOutstandingCount { get; private set; }

        public CoreChallenge SeedOutstanding() => _inner.Issue();

        public ChallengeConsumption ConsumeSeeded(string challenge) =>
            _inner.ConsumeForAttempt(challenge);

        public CoreChallenge Issue()
        {
            IssueCount++;
            return _inner.Issue();
        }

        public ChallengeConsumption ConsumeForAttempt(string challenge)
        {
            ConsumeCount++;
            return _inner.ConsumeForAttempt(challenge);
        }

        public ChallengeAttempt ConsumeOutstandingForAttempt()
        {
            ConsumeOutstandingCount++;
            return _inner.ConsumeOutstandingForAttempt();
        }
    }

    private sealed class BoundaryCatalog : IProcessModeConfigurationCatalog
    {
        private readonly int _candidateCount;

        public BoundaryCatalog(int candidateCount) => _candidateCount = candidateCount;

        public ProcessModeConfigurationCatalogResult GetCatalog()
        {
            if (_candidateCount > ProcessModeConfigurationCatalogContract.MaximumCandidates)
            {
                return ProcessModeConfigurationCatalogResult.Failure(
                    ProcessModeConfigurationCatalogFailureReason.CatalogTooLarge);
            }

            return ProcessModeConfigurationCatalogResult.Success(
                Enumerable.Range(0, _candidateCount)
                    .Select(index => new ProcessModeConfigurationCandidate(
                        $"profile-{index}",
                        "server-0",
                        true,
                        1))
                    .ToArray());
        }

        public ProcessModeConfigurationValidation Validate(
            string profileReference,
            string serverReference) =>
            new(profileReference, serverReference, false, 0, false);
    }

    private sealed class ValidationCatalog : IProcessModeConfigurationCatalog
    {
        private readonly bool _relationshipValid;
        private readonly int _processModeMatchCount;
        private readonly bool _valid;

        public ValidationCatalog(
            bool relationshipValid,
            int processModeMatchCount,
            bool valid) =>
            (_relationshipValid, _processModeMatchCount, _valid) =
                (relationshipValid, processModeMatchCount, valid);

        public ProcessModeConfigurationCatalogResult GetCatalog() =>
            ProcessModeConfigurationCatalogResult.Success(
                Array.Empty<ProcessModeConfigurationCandidate>());

        public ProcessModeConfigurationValidation Validate(
            string profileReference,
            string serverReference) =>
            new(
                profileReference,
                serverReference,
                _relationshipValid,
                _processModeMatchCount,
                _valid);
    }

    private sealed class ThrowingCatalog : IProcessModeConfigurationCatalog
    {
        public ProcessModeConfigurationCatalogResult GetCatalog() =>
            throw new InvalidOperationException("SECRET_EXCEPTION_TEXT");

        public ProcessModeConfigurationValidation Validate(
            string profileReference,
            string serverReference) =>
            throw new InvalidOperationException("SECRET_EXCEPTION_TEXT");
    }

    private sealed class InvalidResultCatalog : IProcessModeConfigurationCatalog
    {
        public ProcessModeConfigurationCatalogResult GetCatalog() =>
            ProcessModeConfigurationCatalogResult.Success(new[]
            {
                new ProcessModeConfigurationCandidate("profile-0", "server-0", false, 2)
            });

        public ProcessModeConfigurationValidation Validate(
            string profileReference,
            string serverReference) =>
            new("profile-1", "server-1", true, 1, true);
    }
}
