namespace OpenTripPlannerAPI.Scrapers.Base;

public sealed class ScrapeResult
{
    public byte[] FeedBytes { get; init; } = [];
    public int ProcessedItems { get; init; }
    public int TotalItems { get; init; }
    public int ItemsWithUpdates { get; init; }
}
