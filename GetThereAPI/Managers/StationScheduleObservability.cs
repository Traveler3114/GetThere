using System.Collections.Concurrent;

namespace GetThereAPI.Managers;

public class StationScheduleObservability
{
    private readonly ConcurrentDictionary<string, int> _counters = new(StringComparer.Ordinal);

    public void Increment(string key)
    {
        _counters.AddOrUpdate(key, 1, (_, value) => value + 1);
    }

    public Dictionary<string, int> Snapshot()
        => _counters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
}
