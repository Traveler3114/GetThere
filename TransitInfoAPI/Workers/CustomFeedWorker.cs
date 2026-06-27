using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Workers;

public class CustomFeedPollingOptions
{
    public int IntervalMinutes { get; set; } = 5;
    public int MaxConsecutiveFailuresBeforeDeactivate { get; set; } = 10;
    public int MaxConcurrency { get; set; } = 5;
}

public class CustomFeedWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<CustomFeedPollingOptions> _options;
    private readonly ILogger<CustomFeedWorker> _logger;
    private readonly ConcurrentDictionary<int, int> _consecutiveFailures = new();

    public CustomFeedWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<CustomFeedPollingOptions> options,
        ILogger<CustomFeedWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Custom feed polling worker started with {Interval}m interval",
            _options.CurrentValue.IntervalMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.IntervalMinutes), ct);
                await PollFeedsAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in custom feed polling cycle");
            }
        }
    }

    private async Task PollFeedsAsync(CancellationToken ct)
    {
        List<CustomFeedPollEntry> dueFeeds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
            var now = DateTime.UtcNow;

            var candidates = await db.CustomFeeds
                .Where(f => f.IsActive)
                .Where(f => f.LastRunAt == null ||
                    !db.CustomFeedRuns.Any(r => r.CustomFeedId == f.Id && r.Status == CustomFeedRunStatus.Running))
                .ToListAsync(ct);

            dueFeeds = candidates
                .Where(f => f.LastRunAt == null ||
                    (now - f.LastRunAt.Value).TotalSeconds >= f.RefreshIntervalSeconds)
                .Select(f => new CustomFeedPollEntry { Id = f.Id })
                .ToList();
        }

        if (dueFeeds.Count == 0)
            return;

        _logger.LogInformation("Found {Count} custom feeds due for refresh", dueFeeds.Count);

        var semaphore = new SemaphoreSlim(_options.CurrentValue.MaxConcurrency);
        var tasks = dueFeeds.Select(async entry =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var manager = scope.ServiceProvider.GetRequiredService<Managers.CustomFeedManager>();
                await manager.ExecuteAsync(entry.Id, ct);

                _consecutiveFailures.TryRemove(entry.Id, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom feed {FeedId} poll failed", entry.Id);

                var failures = _consecutiveFailures.AddOrUpdate(entry.Id, 1, (_, c) => c + 1);
                var maxFailures = _options.CurrentValue.MaxConsecutiveFailuresBeforeDeactivate;

                if (failures >= maxFailures)
                {
                    _logger.LogWarning("Deactivating custom feed {FeedId} after {Failures} consecutive failures",
                        entry.Id, failures);
                    _consecutiveFailures.TryRemove(entry.Id, out _);

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
                        var feed = await db.CustomFeeds.FindAsync(new object[] { entry.Id }, ct);
                        if (feed is not null)
                        {
                            feed.IsActive = false;

                            var hiddenFeed = await db.Feeds
                                .FirstOrDefaultAsync(f => f.CustomFeedId == entry.Id, ct);
                            if (hiddenFeed is not null)
                                hiddenFeed.IsActive = false;

                            await db.SaveChangesAsync(ct);
                        }
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "Failed to deactivate custom feed {FeedId}", entry.Id);
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private class CustomFeedPollEntry
    {
        public int Id { get; set; }
    }
}
