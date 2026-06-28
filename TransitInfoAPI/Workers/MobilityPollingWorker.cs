using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
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
    private readonly FeedSourceFactory _feedSourceFactory;
    private readonly ConcurrentDictionary<int, int> _consecutiveFailures = new();

    public MobilityPollingWorker(
        ILogger<MobilityPollingWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<MobilityPollingOptions> options,
        FeedSourceFactory feedSourceFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options;
        _feedSourceFactory = feedSourceFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Mobility polling worker started with {Interval}s interval",
            _options.CurrentValue.IntervalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.InitialDelaySeconds), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollProvidersAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error during mobility polling cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), ct);
        }
    }

    private async Task PollProvidersAsync(CancellationToken ct)
    {
        List<MobilityProviderEntry> activeProviders;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
            activeProviders = await db.MobilityProviders
                .Where(mp => mp.IsActive)
                .Select(mp => new MobilityProviderEntry { Id = mp.Id })
                .ToListAsync<MobilityProviderEntry>(ct);
        }

        _logger.LogInformation("Checking {Count} active mobility providers", activeProviders.Count);

        foreach (var entry in activeProviders)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mobility = scope.ServiceProvider.GetRequiredService<MobilityManager>();
                await mobility.PollMobilityProviderAsync(entry.Id, ct);
                _consecutiveFailures.TryRemove(entry.Id, out _);
            }
            catch (Exception ex)
            {
                var count = _consecutiveFailures.AddOrUpdate(entry.Id, 1, (_, c) => c + 1);
                _logger.LogWarning(ex, "Failed to poll mobility provider {Id} ({FailCount} consecutive failures)",
                    entry.Id, count);

                var threshold = _options.CurrentValue.MaxConsecutiveFailuresBeforeDeactivate;
                if (count >= threshold)
                {
                    _logger.LogWarning("Auto-deactivating mobility provider {Id} after {Count} consecutive failures",
                        entry.Id, count);
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
                        var provider = await db.MobilityProviders.FindAsync([entry.Id], ct);
                        if (provider is not null)
                        {
                            provider.IsActive = false;
                            await db.SaveChangesAsync(ct);
                        }
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "Failed to deactivate mobility provider {Id}", entry.Id);
                    }
                    _consecutiveFailures.TryRemove(entry.Id, out _);
                }
            }
        }

        // Poll GBFS custom feeds
        List<GbfsCustomFeedEntry> gbfsCustomFeeds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
            gbfsCustomFeeds = await db.Feeds
                .Where(f => f.IsActive && f.FeedType == FeedType.GBFS && f.CustomFeedId != null)
                .Join(db.CustomFeeds, f => f.CustomFeedId, cf => cf.Id, (f, cf) => new GbfsCustomFeedEntry
                {
                    Feed = f,
                    MobilityProviderId = cf.MobilityProviderId
                })
                .Where(x => x.MobilityProviderId != null)
                .ToListAsync(ct);
        }

        if (gbfsCustomFeeds.Count > 0)
        {
            _logger.LogInformation("Polling {Count} GBFS custom feeds", gbfsCustomFeeds.Count);

            foreach (var entry in gbfsCustomFeeds)
            {
                try
                {
                    var source = _feedSourceFactory.Resolve(entry.Feed!);
                    var result = await source.FetchDataAsync(entry.Feed!, ct);

                    if (result.Data.Length > 0)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var mobility = scope.ServiceProvider.GetRequiredService<MobilityManager>();
                        await mobility.UpsertStationsFromGbfsBytesAsync(entry.MobilityProviderId!.Value, result.Data, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to poll GBFS custom feed {FeedId}", entry.Feed?.FeedId);
                }
            }
        }
    }

    private class MobilityProviderEntry
    {
        public int Id { get; set; }
    }

    private class GbfsCustomFeedEntry
    {
        public Entities.Feed? Feed { get; set; }
        public int? MobilityProviderId { get; set; }
    }
}
