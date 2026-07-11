using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Workers;

public class FeedPollingOptions
{
    public int IntervalMinutes { get; set; } = 60;
    public int MaxConsecutiveFailuresBeforeDeactivate { get; set; } = 10;
}

public class FeedPollingWorker : BackgroundService
{
    private readonly ILogger<FeedPollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<FeedPollingOptions> _options;
    private readonly ConcurrentDictionary<int, int> _consecutiveFailures = new();

    public FeedPollingWorker(ILogger<FeedPollingWorker> logger, IServiceScopeFactory scopeFactory, IOptionsMonitor<FeedPollingOptions> options) { _logger = logger; _scopeFactory = scopeFactory; _options = options; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var intervalMinutes = _options.CurrentValue.IntervalMinutes;
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

            intervalMinutes = _options.CurrentValue.IntervalMinutes;
            if (intervalMinutes <= 0) intervalMinutes = 60;
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

        await Parallel.ForEachAsync(staticFeeds, new ParallelOptions
        {
            MaxDegreeOfParallelism = 3,
            CancellationToken = ct
        }, async (feed, innerCt) =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var feedManager = scope.ServiceProvider.GetRequiredService<FeedManager>();
                var newVersion = await feedManager.CheckAndFetchAsync(feed.Id, innerCt);
                if (newVersion is not null)
                {
                    if (newVersion.ImportStatus == FeedImportStatus.Success)
                    {
                        _consecutiveFailures.TryRemove(feed.Id, out _);
                        _logger.LogDebug("Feed {FeedId} already up to date, skipping", feed.FeedId);
                        return;
                    }
                    _logger.LogInformation(
                        "New feed version detected for {FeedId}: {Sha1}, starting import",
                        feed.FeedId, newVersion.Sha1);
                    await feedManager.ImportFeedVersionAsync(newVersion.Id, innerCt);
                }
                _consecutiveFailures.TryRemove(feed.Id, out _);
            }
            catch (Exception ex)
            {
                var count = _consecutiveFailures.AddOrUpdate(feed.Id, 1, (_, c) => c + 1);
                _logger.LogWarning(ex, "Failed to poll/import feed {FeedId} ({FailCount} consecutive failures)", feed.FeedId, count);

                var threshold = _options.CurrentValue.MaxConsecutiveFailuresBeforeDeactivate;
                if (count >= threshold)
                {
                    _logger.LogWarning(
                        "Auto-deactivating feed {FeedId} after {Count} consecutive failures",
                        feed.FeedId, count);
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
                        var dbFeed = await db.Feeds.FindAsync([feed.Id], innerCt);
                        if (dbFeed is not null)
                        {
                            dbFeed.IsActive = false;
                            await db.SaveChangesAsync(innerCt);
                        }
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "Failed to deactivate feed {FeedId}", feed.FeedId);
                    }
                    _consecutiveFailures.TryRemove(feed.Id, out _);
                }
            }
        });
    }
}
