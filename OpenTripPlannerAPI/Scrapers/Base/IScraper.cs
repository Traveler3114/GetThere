namespace OpenTripPlannerAPI.Scrapers.Base;

public interface IScraper
{
    string FeedId { get; }
    bool IsEnabled { get; }
    TimeSpan PollInterval { get; }

    Task InitializeAsync(CancellationToken ct);
    Task<ScrapeResult?> ScrapeAsync(CancellationToken ct);
}
