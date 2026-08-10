namespace NekoProxyCore.Host;

internal sealed class SingleInstanceLease : IDisposable
{
    private readonly Thread _ownerThread;
    private readonly ManualResetEventSlim _releaseRequested;
    private int _disposed;

    private SingleInstanceLease(Thread ownerThread, ManualResetEventSlim releaseRequested)
    {
        _ownerThread = ownerThread;
        _releaseRequested = releaseRequested;
    }

    public static bool TryAcquire(
        out SingleInstanceLease? lease,
        string mutexName = "Local\\NekoProxyCore.s0-rc1")
    {
        if (string.IsNullOrWhiteSpace(mutexName))
            throw new ArgumentException("A mutex name is required.", nameof(mutexName));

        lease = null;
        using var acquisitionCompleted = new ManualResetEventSlim();
        var releaseRequested = new ManualResetEventSlim();
        Exception? acquisitionError = null;
        var acquired = false;

        var ownerThread = new Thread(() =>
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(true, mutexName, out var createdNew);
                if (!createdNew)
                {
                    acquisitionCompleted.Set();
                    return;
                }

                acquired = true;
                acquisitionCompleted.Set();
                releaseRequested.Wait();
                mutex.ReleaseMutex();
            }
            catch (Exception exception)
            {
                acquisitionError = exception;
                acquisitionCompleted.Set();
            }
            finally
            {
                mutex?.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "NekoProxyCore single-instance lease"
        };

        ownerThread.Start();
        acquisitionCompleted.Wait();
        if (acquisitionError != null)
        {
            ownerThread.Join();
            releaseRequested.Dispose();
            throw new InvalidOperationException("The single-instance lease could not be acquired.", acquisitionError);
        }

        if (!acquired)
        {
            ownerThread.Join();
            releaseRequested.Dispose();
            return false;
        }

        lease = new SingleInstanceLease(ownerThread, releaseRequested);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _releaseRequested.Set();
        _ownerThread.Join();
        _releaseRequested.Dispose();
    }
}
