using OpenTripPlannerAPI.Core;

namespace OpenTripPlannerAPI.Scrapers.Base;

public abstract class ScraperBase : IScraper
{
    private readonly ProtobufFeedBuilder _feedBuilder;

    protected ScraperBase(ProtobufFeedBuilder feedBuilder)
    {
        _feedBuilder = feedBuilder;
    }

    public abstract string FeedId { get; }
    public abstract bool IsEnabled { get; }

    public virtual Task InitialiseAsync(CancellationToken ct) => Task.CompletedTask;

    public abstract Task<ScrapeResult> ScrapeAsync(CancellationToken ct);

    protected ScrapeResult BuildResult(
        IReadOnlyDictionary<string, List<StopTimeUpdateData>> updatesMap,
        int processedItems,
        int totalItems,
        int itemsWithUpdates)
    {
        return new ScrapeResult
        {
            FeedBytes = _feedBuilder.BuildFeed(updatesMap).ToByteArray(),
            ProcessedItems = processedItems,
            TotalItems = totalItems,
            ItemsWithUpdates = itemsWithUpdates
        };
    }

    protected ScrapeResult BuildEmptyResult() => new()
    {
        FeedBytes = _feedBuilder.BuildEmptyFeedBytes(),
        ProcessedItems = 0,
        TotalItems = 0,
        ItemsWithUpdates = 0
    };
}
