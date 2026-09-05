namespace NekoProxyCore.Core;

public interface ITelemetryBuffer : IDisposable
{
    int Capacity { get; }
    int Count { get; }
    ulong DroppedEventsCount { get; }
    void Enqueue(string frame);
    bool TryDequeue(out string? frame);
    ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class BoundedTelemetryBuffer : ITelemetryBuffer
{
    public const int DefaultCapacity = 256;

    private readonly int _capacity;
    private readonly Queue<string> _items;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal;
    private long _droppedCount;
    private bool _disposed;

    public BoundedTelemetryBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");

        _capacity = capacity;
        _items = new Queue<string>(capacity);
        _signal = new SemaphoreSlim(0, capacity);
    }

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public ulong DroppedEventsCount => (ulong)Interlocked.Read(ref _droppedCount);

    public void Enqueue(string frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));

        lock (_gate)
        {
            if (_disposed)
                return;

            if (_items.Count >= _capacity)
            {
                _items.Dequeue();
                Interlocked.Increment(ref _droppedCount);
                _items.Enqueue(frame);
                // The item count remains unchanged at capacity, so the semaphore count is already accurate.
            }
            else
            {
                _items.Enqueue(frame);
                _signal.Release();
            }
        }
    }

    public bool TryDequeue(out string? frame)
    {
        lock (_gate)
        {
            if (!_disposed && _items.Count > 0 && _signal.Wait(0))
            {
                frame = _items.Dequeue();
                return true;
            }

            frame = null;
            return false;
        }
    }

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BoundedTelemetryBuffer));

            return _items.Dequeue();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _items.Clear();
            _signal.Dispose();
        }
    }
}
