namespace OpenTripPlannerAPI.Scrapers.Base;

public sealed class ScrapeResult
{
    public required byte[] FeedBytes { get; init; }
    public ScrapeProgress? Progress { get; init; }
}

public sealed class ScrapeProgress
{
    public int TotalItems { get; init; }
    public int ProcessedItems { get; init; }
    public int ItemsWithUpdates { get; init; }
}
