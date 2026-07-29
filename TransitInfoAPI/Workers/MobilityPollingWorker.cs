using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Workers;

public class MobilityPollingOptions
{
    public int IntervalSeconds { get; set; } = 120;
    public int MaxConsecutiveFailuresBeforeDeactivate { get; set; } = 10;
    public int InitialDelaySeconds { get; set; } = 15;
}

public class MobilityPollingWorker : BackgroundService
{
    private readonly ILogger<MobilityPollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<MobilityPollingOptions> _options;
    private readonly ExternalFeedSource _externalFeedSource;

    /// <summary>
    /// Consecutive failures per feed. MaxConsecutiveFailuresBeforeDeactivate was declared and bound
    /// but never read here — unlike FeedPollingWorker and RealtimeManager, which both count and
    /// deactivate — so a permanently broken GBFS feed was retried every cycle forever.
    /// </summary>
    private readonly ConcurrentDictionary<int, int> _consecutiveFailures = new();

    public MobilityPollingWorker(ILogger<MobilityPollingWorker> logger, IServiceScopeFactory scopeFactory, IOptionsMonitor<MobilityPollingOptions> options, ExternalFeedSource externalFeedSource) { _logger = logger; _scopeFactory = scopeFactory; _options = options; _externalFeedSource = externalFeedSource; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Mobility polling worker started with {Interval}s interval",
            _options.CurrentValue.IntervalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.InitialDelaySeconds), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollGbfsFeedsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error during mobility polling cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), ct);
        }
    }

    private async Task PollGbfsFeedsAsync(CancellationToken ct)
    {
        List<FeedEntry> gbfsFeeds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
            gbfsFeeds = await db.Feeds
                .Where(f => f.IsActive && f.FeedType == FeedType.GBFS && f.Url != null)
                .Select(f => new FeedEntry
                {
                    Feed = f,
                    OperatorId = f.OperatorId
                })
                .ToListAsync(ct);
        }

        if (gbfsFeeds.Count > 0)
        {
            _logger.LogInformation("Polling {Count} GBFS feeds", gbfsFeeds.Count);

            foreach (var entry in gbfsFeeds)
            {
                try
                {
                    var result = await _externalFeedSource.FetchDataAsync(entry.Feed!, ct);

                    if (result.Data.Length > 0)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var mobility = scope.ServiceProvider.GetRequiredService<MobilityManager>();
                        await mobility.UpsertStationsFromGbfsBytesAsync(entry.OperatorId, result.Data, ct);
                    }

                    _consecutiveFailures.TryRemove(entry.Feed!.Id, out _);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var feedId = entry.Feed!.Id;
                    var count = _consecutiveFailures.AddOrUpdate(feedId, 1, (_, c) => c + 1);
                    _logger.LogWarning(ex, "Failed to poll GBFS feed {FeedId} ({FailCount} consecutive failures)",
                        entry.Feed?.FeedId, count);

                    var threshold = _options.CurrentValue.MaxConsecutiveFailuresBeforeDeactivate;
                    if (threshold > 0 && count >= threshold)
                    {
                        _logger.LogWarning("Auto-deactivating GBFS feed {FeedId} after {Count} consecutive failures",
                            entry.Feed?.FeedId, count);
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
                            var dbFeed = await db.Feeds.FindAsync([feedId], ct);
                            if (dbFeed is not null)
                            {
                                dbFeed.IsActive = false;
                                await db.SaveChangesAsync(ct);
                            }
                        }
                        catch (Exception inner) when (inner is not OperationCanceledException)
                        {
                            _logger.LogError(inner, "Failed to deactivate GBFS feed {FeedId}", entry.Feed?.FeedId);
                        }
                        _consecutiveFailures.TryRemove(feedId, out _);
                    }
                }
            }
        }
    }

    private class FeedEntry
    {
        public Entities.Feed? Feed { get; set; }
        public int OperatorId { get; set; }
    }
}
