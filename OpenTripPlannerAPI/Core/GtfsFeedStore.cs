using System.Collections.Concurrent;

namespace OpenTripPlannerAPI.Core;

public sealed class GtfsFeedStore
{
    private readonly ConcurrentDictionary<string, FeedState> _feeds = new(StringComparer.OrdinalIgnoreCase);

    public void Update(string feedId, byte[] bytes)
    {
        var now = DateTime.UtcNow;
        _feeds.AddOrUpdate(
            feedId,
            _ => new FeedState
            {
                Bytes = bytes,
                LastUpdated = now,
                IsHealthy = true,
                LastError = null
            },
            (_, existing) => existing with
            {
                Bytes = bytes,
                LastUpdated = now,
                IsHealthy = true,
                LastError = null
            });
    }

    public void MarkFailure(string feedId, string? error)
    {
        _feeds.AddOrUpdate(
            feedId,
            _ => new FeedState
            {
                Bytes = [],
                LastUpdated = DateTime.MinValue,
                IsHealthy = false,
                LastError = error
            },
            (_, existing) => existing with
            {
                IsHealthy = false,
                LastError = error
            });
    }

    public FeedState Read(string feedId)
        => _feeds.TryGetValue(feedId, out var value) ? value : FeedState.Empty;

    public IReadOnlyDictionary<string, FeedState> ReadAll()
        => new Dictionary<string, FeedState>(_feeds, StringComparer.OrdinalIgnoreCase);
}

public sealed record FeedState
{
    public static FeedState Empty { get; } = new();

    public byte[] Bytes { get; init; } = [];
    public DateTime LastUpdated { get; init; } = DateTime.MinValue;
    public bool IsHealthy { get; init; }
    public string? LastError { get; init; }
}
