namespace NekoProxyCore.Host;

internal sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstanceLease(Mutex mutex) => _mutex = mutex;

    public static bool TryAcquire(out SingleInstanceLease? lease)
    {
        var mutex = new Mutex(true, "Local\\NekoProxyCore.s0-rc1", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            lease = null;
            return false;
        }

        lease = new SingleInstanceLease(mutex);
        return true;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
