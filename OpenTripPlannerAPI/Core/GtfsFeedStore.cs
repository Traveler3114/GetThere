using System.Collections.Concurrent;

namespace OpenTripPlannerAPI.Core;

public sealed class GtfsFeedStore
{
    private readonly ConcurrentDictionary<string, FeedSnapshot> _feeds = new(StringComparer.OrdinalIgnoreCase);

    public void Update(string feedId, byte[] bytes, int processedItems, int totalItems, int itemsWithUpdates)
    {
        var snapshot = new FeedSnapshot
        {
            Bytes = bytes,
            LastUpdated = DateTime.UtcNow,
            ProcessedItems = processedItems,
            TotalItems = totalItems,
            ItemsWithUpdates = itemsWithUpdates
        };

        _feeds[feedId] = snapshot;
    }

    public FeedSnapshot? Read(string feedId)
    {
        return _feeds.TryGetValue(feedId, out var snapshot) ? snapshot : null;
    }

    public IReadOnlyDictionary<string, FeedSnapshot> ReadAll() => _feeds;
}

public sealed class FeedSnapshot
{
    public byte[] Bytes { get; set; } = [];
    public DateTime LastUpdated { get; set; }
    public int ProcessedItems { get; set; }
    public int TotalItems { get; set; }
    public int ItemsWithUpdates { get; set; }
}
