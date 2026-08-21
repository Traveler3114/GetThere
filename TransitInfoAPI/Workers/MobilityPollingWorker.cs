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
    private readonly ConcurrentDictionary<int, int> _customSourceFailures = new();

    public MobilityPollingWorker(ILogger<MobilityPollingWorker> logger, IServiceScopeFactory scopeFactory, IOptionsMonitor<MobilityPollingOptions> options, ExternalFeedSource externalFeedSource) { _logger = logger; _scopeFactory = scopeFactory; _options = options; _externalFeedSource = externalFeedSource; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Clamped rather than passed through — see PollingInterval and RealtimePollingWorker.
        var initialDelay = PollingInterval.InitialDelaySeconds(
            _options.CurrentValue.InitialDelaySeconds, 15, _logger, "MobilityPolling:InitialDelaySeconds");

        _logger.LogInformation("Mobility polling worker started with {Interval}s interval",
            _options.CurrentValue.IntervalSeconds);

        await Task.Delay(initialDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollGbfsFeedsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error during GBFS mobility polling cycle");
            }

            try
            {
                await PollCustomMobilitySourcesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error during custom mobility polling cycle");
            }

            // Refresh only the GBFS payloads (cache + three JSON files) without rebuilding gtfs.zip.
            // Bike availability is realtime-ish and OTP re-polls GBFS every minute, so a cheap per-poll
            // refresh is the right shape. Failures here must not kill the poll loop.
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gbfsRefresh = scope.ServiceProvider.GetRequiredService<TransitInfoAPI.Routing.GbfsRefreshService>();
                await gbfsRefresh.RefreshAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GBFS refresh after mobility poll failed");
            }

            await Task.Delay(
                PollingInterval.Seconds(_options.CurrentValue.IntervalSeconds, 120, _logger, "MobilityPolling:IntervalSeconds"),
                ct);
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

    private async Task PollCustomMobilitySourcesAsync(CancellationToken ct)
    {
        List<Entities.CustomSource> sources;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
            sources = await db.CustomSources
                .Include(cs => cs.Requests).ThenInclude(r => r.Mappings)
                .AsSplitQuery()
                .Where(cs => cs.IsActive && cs.ProducesMobility)
                .ToListAsync(ct);
        }

        if (sources.Count == 0) return;

        _logger.LogInformation("Polling {Count} custom mobility source(s)", sources.Count);

        foreach (var source in sources)
        {
            try
            {
                List<Dictionary<string, object?>> records;
                using (var scope = _scopeFactory.CreateScope())
                {
                    // Generic declarative only — no per-source C# code.
                    var engine = scope.ServiceProvider.GetRequiredService<CustomSourceEngine>();
                    var protector = scope.ServiceProvider.GetRequiredService<Services.SecretProtector>();
                    var all = new List<Dictionary<string, object?>>();
                    var auth = protector.Unprotect(source.AuthConfig);
                    foreach (var req in source.Requests.OrderBy(r => r.SortOrder))
                    {
                        var extraction = await engine.ExecuteAsync(req, auth, ct: ct);
                        var mapped = CustomSourceEngine.ApplyMappings(extraction.Rows, [.. req.Mappings]);
                        mapped = CustomSourceEngine.Deduplicate(mapped, req.DistinctBy, out var dropped);
                        if (dropped > 0)
                            _logger.LogInformation("Custom mobility source {SourceId} dropped {Dropped} duplicate row(s) on {DistinctBy}", source.Id, dropped, req.DistinctBy);
                        all.AddRange(mapped.Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)));
                    }
                    records = all;
                }

                if (records.Count > 0)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var mobility = scope.ServiceProvider.GetRequiredService<MobilityManager>();
                    await mobility.UpsertStationsFromRecordsAsync(source.OperatorId, records, ct);
                    _logger.LogInformation("Upserted {Count} mobility stations from custom source {SourceId} ({Name})", records.Count, source.Id, source.Name);
                }
                else
                {
                    _logger.LogInformation("Custom mobility source {SourceId} ({Name}) produced 0 records", source.Id, source.Name);
                }

                // Record success: reset failure count and update LastRunAt
                _customSourceFailures.TryRemove(source.Id, out _);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
                    var dbSource = await db.CustomSources.FindAsync([source.Id], ct);
                    if (dbSource is not null)
                    {
                        dbSource.LastRunAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception inner) when (inner is not OperationCanceledException)
                {
                    _logger.LogWarning(inner, "Failed to update LastRunAt for custom mobility source {SourceId}", source.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var count = _customSourceFailures.AddOrUpdate(source.Id, 1, (_, c) => c + 1);
                _logger.LogWarning(ex, "Failed to poll custom mobility source {SourceId} ({Name}) ({FailCount} consecutive failures)", source.Id, source.Name, count);

                var threshold = _options.CurrentValue.MaxConsecutiveFailuresBeforeDeactivate;
                if (threshold > 0 && count >= threshold)
                {
                    _logger.LogWarning("Auto-deactivating custom mobility source {SourceId} after {Count} consecutive failures", source.Id, count);
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
                        var dbSource = await db.CustomSources.FindAsync([source.Id], ct);
                        if (dbSource is not null)
                        {
                            dbSource.IsActive = false;
                            await db.SaveChangesAsync(ct);
                        }
                    }
                    catch (Exception inner) when (inner is not OperationCanceledException)
                    {
                        _logger.LogError(inner, "Failed to deactivate custom mobility source {SourceId}", source.Id);
                    }
                    _customSourceFailures.TryRemove(source.Id, out _);
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
