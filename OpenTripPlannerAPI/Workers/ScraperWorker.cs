using OpenTripPlannerAPI.Core;
using OpenTripPlannerAPI.Scrapers.Base;

namespace OpenTripPlannerAPI.Workers;

public sealed class ScraperWorker : BackgroundService
{
    private readonly ILogger<ScraperWorker> _logger;
    private readonly IReadOnlyList<IScraper> _scrapers;
    private readonly GtfsFeedStore _feedStore;
    private readonly GtfsReadySignal _readySignal;
    private readonly IConfiguration _config;

    public ScraperWorker(
        ILogger<ScraperWorker> logger,
        IEnumerable<IScraper> scrapers,
        GtfsFeedStore feedStore,
        GtfsReadySignal readySignal,
        IConfiguration config)
    {
        _logger = logger;
        _scrapers = scrapers.ToList();
        _feedStore = feedStore;
        _readySignal = readySignal;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabledScrapers = _scrapers.Where(s => s.IsEnabled).ToList();
        if (enabledScrapers.Count == 0)
        {
            _logger.LogInformation("No enabled scrapers found; worker is idle.");
            _readySignal.SetReady();
            return;
        }

        foreach (var scraper in enabledScrapers)
            await scraper.InitialiseAsync(stoppingToken);

        var interval = int.Parse(_config["Scrape:IntervalSeconds"] ?? "30");
        _logger.LogInformation("Scraper loop started — interval={Interval}s, enabled={Count}", interval, enabledScrapers.Count);

        var firstCycle = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var scraper in enabledScrapers)
            {
                try
                {
                    var result = await scraper.ScrapeAsync(stoppingToken);
                    _feedStore.Update(scraper.FeedId, result.FeedBytes, result.ProcessedItems, result.TotalItems, result.ItemsWithUpdates);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scrape error for feed {FeedId}", scraper.FeedId);
                }
            }

            if (firstCycle)
            {
                firstCycle = false;
                _readySignal.SetReady();
            }

            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }
}
