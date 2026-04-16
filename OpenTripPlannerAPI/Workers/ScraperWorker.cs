using OpenTripPlannerAPI.Core;
using OpenTripPlannerAPI.Scrapers.Base;

namespace OpenTripPlannerAPI.Workers;

public class ScraperWorker : BackgroundService
{
    private readonly ILogger<ScraperWorker> _logger;
    private readonly IReadOnlyList<IScraper> _scrapers;
    private readonly GtfsFeedStore _feedStore;
    private readonly GtfsReadySignal _readySignal;

    public ScraperWorker(
        ILogger<ScraperWorker> logger,
        IEnumerable<IScraper> scrapers,
        GtfsFeedStore feedStore,
        GtfsReadySignal readySignal)
    {
        _logger = logger;
        _scrapers = scrapers.ToList();
        _feedStore = feedStore;
        _readySignal = readySignal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabledScrapers = _scrapers.Where(s => s.IsEnabled).ToList();
        if (enabledScrapers.Count == 0)
        {
            _logger.LogInformation("No enabled scrapers found; scraper worker is idle.");
            return;
        }

        var nextRunTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var readyFeedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scraper in enabledScrapers)
        {
            try
            {
                await scraper.InitializeAsync(stoppingToken);
                nextRunTimes[scraper.FeedId] = DateTimeOffset.UtcNow;
                _logger.LogInformation("Initialized scraper for feed '{FeedId}'.", scraper.FeedId);
            }
            catch (Exception ex)
            {
                _feedStore.MarkFailure(scraper.FeedId, ex.Message);
                _logger.LogError(ex, "Failed to initialize scraper for feed '{FeedId}'.", scraper.FeedId);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var scraper in enabledScrapers)
            {
                if (!nextRunTimes.TryGetValue(scraper.FeedId, out var scheduledAt) || now < scheduledAt)
                    continue;

                nextRunTimes[scraper.FeedId] = now.Add(scraper.PollInterval);

                try
                {
                    var result = await scraper.ScrapeAsync(stoppingToken);
                    if (result?.FeedBytes is { Length: > 0 } bytes)
                    {
                        _feedStore.Update(scraper.FeedId, bytes, result.Progress);

                        if (result.Progress is { } progress)
                        {
                            _logger.LogInformation(
                                "Feed '{FeedId}' scraped {Processed}/{Total} items ({WithUpdates} with updates).",
                                scraper.FeedId,
                                progress.ProcessedItems,
                                progress.TotalItems,
                                progress.ItemsWithUpdates);
                        }

                        if (readyFeedIds.Add(scraper.FeedId))
                            _readySignal.SetReady(scraper.FeedId);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _feedStore.MarkFailure(scraper.FeedId, ex.Message);
                    _logger.LogError(ex, "Scrape failed for feed '{FeedId}'.", scraper.FeedId);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
