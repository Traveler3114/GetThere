namespace TransitInfoAPI.Core;

public record FeedFetchResult(
    byte[] Data,
    string? ContentType,
    string? ETag,
    DateTime? LastModified
)
{
    public bool AlreadyHandled { get; init; }
}

public interface IFeedSource
{
    Task<FeedFetchResult> FetchDataAsync(Entities.Feed feed, CancellationToken ct = default);
    string ComputeHash(Entities.Feed feed, byte[] data);
}
