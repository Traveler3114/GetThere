using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Workers;

public class RealtimePollingOptions
{
    public int IntervalSeconds { get; set; } = 30;
    public int MaxConsecutiveFailuresBeforeDeactivate { get; set; } = 10;
}

public class RealtimePollingWorker : BackgroundService
{
    private readonly ILogger<RealtimePollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RealtimePollingOptions> _options;
    private readonly ConcurrentDictionary<int, int> _consecutiveFailures = new();

    public RealtimePollingWorker(
        ILogger<RealtimePollingWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<RealtimePollingOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Realtime polling worker started with {Interval}s interval", _options.CurrentValue.IntervalSeconds);

        // Initial delay to let the app settle
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var realtime = scope.ServiceProvider.GetRequiredService<RealtimeManager>();
                await realtime.PollAllFeedsAsync(ct);
                _consecutiveFailures.Clear();
            }
            catch (Exception ex)
            {
                var cycleKey = -1;
                var count = _consecutiveFailures.AddOrUpdate(cycleKey, 1, (_, c) => c + 1);
                _logger.LogWarning(ex, "Error during realtime polling cycle ({FailCount} consecutive failures)", count);

                var threshold = _options.CurrentValue.MaxConsecutiveFailuresBeforeDeactivate;
                if (count >= threshold)
                {
                    _logger.LogError(
                        "Realtime polling stopped after {Count} consecutive failures; worker will continue but errors persist",
                        count);
                    _consecutiveFailures.TryRemove(cycleKey, out _);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), ct);
        }
    }
}
