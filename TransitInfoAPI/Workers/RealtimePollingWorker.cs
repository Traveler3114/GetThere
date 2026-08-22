using Microsoft.Extensions.Options;

using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Workers;

public class RealtimePollingOptions
{
    public int IntervalSeconds { get; set; } = 30;
    public int MaxConsecutiveFailuresBeforeDeactivate { get; set; } = 10;
    public int VehicleStaleCutoffMinutes { get; set; } = 5;
    public int InitialDelaySeconds { get; set; } = 10;
    public int StaleAfterMinutes { get; set; } = 15;
}

public class RealtimePollingWorker : BackgroundService
{
    private readonly ILogger<RealtimePollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RealtimePollingOptions> _options;

    public RealtimePollingWorker(ILogger<RealtimePollingWorker> logger, IServiceScopeFactory scopeFactory, IOptionsMonitor<RealtimePollingOptions> options) { _logger = logger; _scopeFactory = scopeFactory; _options = options; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Clamped rather than passed through. See PollingInterval: a zero here spun the loop, and a
        // negative threw out of ExecuteAsync and stopped the whole host.
        var initialDelay = PollingInterval.InitialDelaySeconds(
            _options.CurrentValue.InitialDelaySeconds, 10, _logger, "RealtimePolling:InitialDelaySeconds");

        _logger.LogInformation("Realtime polling worker started with {Interval}s interval", _options.CurrentValue.IntervalSeconds);

        await Task.Delay(initialDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var realtime = scope.ServiceProvider.GetRequiredService<RealtimeManager>();
                await realtime.PollAllFeedsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error during realtime polling cycle");
            }

            // Re-read each cycle so a configuration change takes effect without a restart — which is
            // also why the clamp has to be here rather than only in the constructor.
            await Task.Delay(
                PollingInterval.Seconds(_options.CurrentValue.IntervalSeconds, 30, _logger, "RealtimePolling:IntervalSeconds"),
                ct);
        }
    }
}
