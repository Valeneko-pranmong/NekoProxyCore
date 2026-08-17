using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host;
using NekoProxyCore.Host.Protocol;

namespace Tests;

[TestClass]
[DoNotParallelize]
public sealed class HeadlessTelemetryServerLifecycleTests
{
    [TestMethod]
    public void CanonicalPipeNameIsUsed()
    {
        Assert.AreEqual("NekoProxyCoreTelemetry", TelemetryProtocol.PipeName);
        Assert.AreEqual("NekoProxyCoreTelemetry", HeadlessTelemetryServer.PipeName);
    }

    [TestMethod]
    public async Task ConsumerConnectReceivesPublishedFrames()
    {
        var pipeName = UniquePipeName();
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var server = new HeadlessTelemetryServer(buffer, pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = server.RunAsync(cts.Token);

        publisher.PublishLifecycle("core.started", "core");

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000, cts.Token);
        using var reader = new StreamReader(client, Encoding.UTF8);

        var received = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2), cts.Token);
        Assert.IsNotNull(received);
        StringAssert.Contains(received, "\"message_type\":\"core.started\"");

        cts.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task ConsumerAbsentDoesNotBlockPublisherOrServer()
    {
        var pipeName = UniquePipeName();
        using var buffer = new BoundedTelemetryBuffer(5);
        var publisher = new TelemetryPublisher(buffer);
        var server = new HeadlessTelemetryServer(buffer, pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = server.RunAsync(cts.Token);

        // Enqueue more than capacity while no consumer is connected
        for (var i = 0; i < 20; i++)
        {
            publisher.PublishLifecycle($"event.{i}", "core");
        }

        Assert.AreEqual(5, buffer.Count);
        Assert.AreEqual((ulong)15, buffer.DroppedEventsCount);
        Assert.IsFalse(serverTask.IsCompleted);

        cts.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task ConsumerDisconnectIsHandledGracefully()
    {
        var pipeName = UniquePipeName();
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var server = new HeadlessTelemetryServer(buffer, pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = server.RunAsync(cts.Token);

        publisher.PublishLifecycle("msg.1", "core");

        // Connect client, read one frame, then abruptly close client
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000, cts.Token);
        var reader = new StreamReader(client, Encoding.UTF8);
        var line1 = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2), cts.Token);
        Assert.IsNotNull(line1);

        // Abrupt disconnect
        reader.Dispose();
        await client.DisposeAsync();

        // Enqueue next frames; server should handle broken pipe without crashing
        publisher.PublishLifecycle("msg.2", "core");
        publisher.PublishLifecycle("msg.3", "core");

        await Task.Delay(100, cts.Token);
        Assert.IsFalse(serverTask.IsCompleted);

        cts.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task ConsumerCanReconnectAfterDisconnect()
    {
        var pipeName = UniquePipeName();
        using var buffer = new BoundedTelemetryBuffer(10);
        var publisher = new TelemetryPublisher(buffer);
        var server = new HeadlessTelemetryServer(buffer, pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = server.RunAsync(cts.Token);

        publisher.PublishLifecycle("first.connect.msg", "core");

        // First client
        await using (var client1 = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous))
        {
            await client1.ConnectAsync(2000, cts.Token);
            using var reader1 = new StreamReader(client1, Encoding.UTF8);
            var line = await reader1.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2), cts.Token);
            StringAssert.Contains(line, "\"message_type\":\"first.connect.msg\"");
        }

        // Trigger broken pipe detection on old connection by enqueueing a message
        publisher.PublishLifecycle("disconnect.trigger", "core");
        await Task.Delay(100, cts.Token);

        // Second client connects
        await using (var client2 = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous))
        {
            await client2.ConnectAsync(2000, cts.Token);
            publisher.PublishLifecycle("second.connect.msg", "core");
            using var reader2 = new StreamReader(client2, Encoding.UTF8);
            var line = await reader2.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2), cts.Token);
            Assert.IsNotNull(line);
            StringAssert.Contains(line, "\"message_type\":\"second.connect.msg\"");
        }

        cts.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TelemetryServerShutsDownCleanlyOnCancellation()
    {
        var pipeName = UniquePipeName();
        using var buffer = new BoundedTelemetryBuffer(10);
        var server = new HeadlessTelemetryServer(buffer, pipeName);
        using var cts = new CancellationTokenSource();

        var serverTask = server.RunAsync(cts.Token);

        cts.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(serverTask.IsCompletedSuccessfully);
    }

    private static string UniquePipeName() =>
        $"{TelemetryProtocol.PipeName}-test-{Guid.NewGuid():N}";
}
