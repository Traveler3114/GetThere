using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Workers;

public class FeedPollingWorker : BackgroundService
{
    private readonly ILogger<FeedPollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;

    public FeedPollingWorker(
        ILogger<FeedPollingWorker> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration config)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var intervalMinutes = _config.GetValue<int>("FeedPolling:IntervalMinutes", 60);
        if (intervalMinutes <= 0) intervalMinutes = 60;

        _logger.LogInformation("Feed polling worker started with {Interval} min interval", intervalMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollFeeds(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during feed polling cycle");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), ct);
        }
    }

    private async Task PollFeeds(CancellationToken ct)
    {
        List<Feed> staticFeeds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var feedManager = scope.ServiceProvider.GetRequiredService<FeedManager>();
            staticFeeds = await feedManager.GetActiveGtfsFeedsAsync(ct);
        }

        _logger.LogInformation("Checking {Count} active GTFS-static feeds", staticFeeds.Count);

        foreach (var feed in staticFeeds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var feedManager = scope.ServiceProvider.GetRequiredService<FeedManager>();
                var newVersion = await feedManager.CheckAndFetchAsync(feed.Id, ct);
                if (newVersion != null)
                {
                    if (newVersion.ImportStatus == FeedImportStatus.Success)
                    {
                        _logger.LogDebug("Feed {FeedId} already up to date, skipping", feed.FeedId);
                        continue;
                    }
                    _logger.LogInformation(
                        "New feed version detected for {FeedId}: {Sha1}, starting import",
                        feed.FeedId, newVersion.Sha1);
                    await feedManager.ImportFeedVersionAsync(newVersion.Id, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll/import feed {FeedId}", feed.FeedId);
            }
        }
    }
}
