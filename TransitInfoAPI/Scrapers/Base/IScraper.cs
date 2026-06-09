namespace TransitInfoAPI.Scrapers.Base;

public interface IScraper
{
    string FeedId { get; }
    bool IsEnabled { get; }
    Task InitialiseAsync(CancellationToken ct);
    Task<ScrapeResult> ScrapeAsync(CancellationToken ct);
}
