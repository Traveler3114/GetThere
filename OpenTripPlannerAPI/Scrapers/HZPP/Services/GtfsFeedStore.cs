namespace OpenTripPlannerAPI.Scrapers.HZPP.Services;

public sealed class GtfsFeedStore
{
    private readonly ReaderWriterLockSlim _lock = new();
    private byte[] _feedBytes = [];
    private DateTime _lastUpdated = DateTime.MinValue;

    public void Update(byte[] bytes)
    {
        _lock.EnterWriteLock();
        try { _feedBytes = bytes; _lastUpdated = DateTime.UtcNow; }
        finally { _lock.ExitWriteLock(); }
    }

    public (byte[] Bytes, DateTime LastUpdated) Read()
    {
        _lock.EnterReadLock();
        try { return (_feedBytes, _lastUpdated); }
        finally { _lock.ExitReadLock(); }
    }
}
