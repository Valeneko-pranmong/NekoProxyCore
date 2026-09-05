using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class BoundedTelemetryBufferTests
{
    [TestMethod]
    public void CapacityIsBoundedToConfiguredLimit()
    {
        var buffer = new BoundedTelemetryBuffer(capacity: 5);
        Assert.AreEqual(5, buffer.Capacity);
        Assert.AreEqual(0, buffer.Count);
        Assert.AreEqual((ulong)0, buffer.DroppedEventsCount);
    }

    [TestMethod]
    public void DropOldestPolicyDropsOldestItemsWhenFull()
    {
        var buffer = new BoundedTelemetryBuffer(capacity: 3);

        buffer.Enqueue("frame_1");
        buffer.Enqueue("frame_2");
        buffer.Enqueue("frame_3");

        Assert.AreEqual(3, buffer.Count);
        Assert.AreEqual((ulong)0, buffer.DroppedEventsCount);

        // Enqueue 4th frame, frame_1 must be dropped
        buffer.Enqueue("frame_4");

        Assert.AreEqual(3, buffer.Count);
        Assert.AreEqual((ulong)1, buffer.DroppedEventsCount);

        // Dequeue should yield frame_2, frame_3, frame_4
        Assert.IsTrue(buffer.TryDequeue(out var out1));
        Assert.AreEqual("frame_2", out1);

        Assert.IsTrue(buffer.TryDequeue(out var out2));
        Assert.AreEqual("frame_3", out2);

        Assert.IsTrue(buffer.TryDequeue(out var out3));
        Assert.AreEqual("frame_4", out3);

        Assert.IsFalse(buffer.TryDequeue(out _));
        Assert.AreEqual(0, buffer.Count);
        Assert.AreEqual((ulong)1, buffer.DroppedEventsCount);
    }

    [TestMethod]
    public void MassiveOverflowIncrementsDroppedCounterAccurately()
    {
        var buffer = new BoundedTelemetryBuffer(capacity: 10);

        for (var i = 0; i < 110; i++)
        {
            buffer.Enqueue($"item_{i}");
        }

        Assert.AreEqual(10, buffer.Count);
        Assert.AreEqual((ulong)100, buffer.DroppedEventsCount);

        // The remaining items should be item_100 through item_109
        for (var i = 100; i < 110; i++)
        {
            Assert.IsTrue(buffer.TryDequeue(out var item));
            Assert.AreEqual($"item_{i}", item);
        }

        Assert.IsFalse(buffer.TryDequeue(out _));
    }

    [TestMethod]
    public async Task ConcurrentEnqueueAndDequeueNeverDeadlocks()
    {
        var buffer = new BoundedTelemetryBuffer(capacity: 50);
        var totalEnqueue = 500;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var producerTask = Task.Run(() =>
        {
            for (var i = 0; i < totalEnqueue; i++)
            {
                buffer.Enqueue($"payload_{i}");
            }
        });

        var dequeuedCount = 0;
        var consumerTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested && dequeuedCount + (int)buffer.DroppedEventsCount < totalEnqueue)
            {
                if (buffer.TryDequeue(out _))
                {
                    dequeuedCount++;
                }
                else
                {
                    await Task.Yield();
                }
            }
        });

        await Task.WhenAll(producerTask, consumerTask);
        Assert.AreEqual(totalEnqueue, dequeuedCount + buffer.Count + (int)buffer.DroppedEventsCount);
    }
}
