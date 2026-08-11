using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host;

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
        var server = new HeadlessControlServer(runtime, new CoreChallengeService(), shutdown, UniquePipeName());
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
        var server = new HeadlessControlServer(runtime, new CoreChallengeService(), shutdown, UniquePipeName());
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
        var server = new HeadlessControlServer(runtime, new CoreChallengeService(), shutdown, UniquePipeName());
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
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(
            runtime,
            new CoreChallengeService(),
            shutdown,
            catalog,
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
    public async Task FailedRuntimeStopDoesNotAcknowledgeOrCancelHost()
    {
        var runtime = new RecordingRuntime(ProxyStatusKind.Running, stopSucceeds: false);
        using var shutdown = new HostShutdownSignal();
        var server = new HeadlessControlServer(runtime, new CoreChallengeService(), shutdown, UniquePipeName());
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

    private sealed class ThrowingCatalog : IProcessModeConfigurationCatalog
    {
        public ProcessModeConfigurationCatalogResult GetCatalog() =>
            throw new InvalidOperationException("SECRET_EXCEPTION_TEXT");

        public ProcessModeConfigurationValidation Validate(
            string profileReference,
            string serverReference) =>
            throw new InvalidOperationException("SECRET_EXCEPTION_TEXT");
    }
}