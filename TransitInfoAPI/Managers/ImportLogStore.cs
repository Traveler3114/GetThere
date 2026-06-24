using System.Collections.Concurrent;

namespace TransitInfoAPI.Services;

// In-memory store — entries are lost on restart.
// Acceptable: import logs are ephemeral debug info, not audit records.
// Revisit for multi-instance deployment (need distributed cache or DB persistence).
public class ImportLogStore
{
    private readonly ConcurrentDictionary<int, List<string>> _logs = new();
    private const int MaxEntries = 500;

    public void AddEntry(int versionId, string message)
    {
        var list = _logs.GetOrAdd(versionId, _ => []);
        lock (list)
        {
            list.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
            if (list.Count > MaxEntries)
                list.RemoveAt(0);
        }
    }

    public List<string> GetEntries(int versionId)
    {
        if (_logs.TryGetValue(versionId, out var list))
        {
            lock (list)
            {
                return [.. list];
            }
        }
        return [];
    }

    public void Clear(int versionId)
    {
        _logs.TryRemove(versionId, out _);
    }

    public void ClearAll()
    {
        _logs.Clear();
    }
}
