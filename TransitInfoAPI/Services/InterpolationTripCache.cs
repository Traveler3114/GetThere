using System.Collections.Concurrent;

namespace TransitInfoAPI.Services;

/// <summary>
/// Per-service-day stop times for interpolated trips, held across polls.
/// <para>
/// A singleton because the extractor that uses it is scoped and is resolved from a new scope on
/// every poll — instance state there is built and discarded every 30 seconds, which is what this
/// replaces. Deliberately not <c>IMemoryCache</c>: that instance has <c>SizeLimit = 2_000</c>
/// shared with <c>ScheduleManager</c>'s service-calendar entries, and a large feed's trips would
/// evict them.
/// </para>
/// </summary>
public sealed class InterpolationTripCache
{
    public sealed record TripStop(int StopSequence, int ArrivalTime, int DepartureTime, double Lat, double Lon);

    private readonly ConcurrentDictionary<string, IReadOnlyList<TripStop>> _byKey = new(StringComparer.Ordinal);
    private string _day = string.Empty;
    private readonly object _dayLock = new();

    /// <summary>Drops everything when the service day rolls over.</summary>
    public void EnsureDay(string dayKey)
    {
        lock (_dayLock)
        {
            if (_day == dayKey) return;
            _byKey.Clear();
            _day = dayKey;
        }
    }

    public bool TryGet(string key, out IReadOnlyList<TripStop> stops) => _byKey.TryGetValue(key, out stops!);

    public void Set(string key, IReadOnlyList<TripStop> stops) => _byKey[key] = stops;
}
