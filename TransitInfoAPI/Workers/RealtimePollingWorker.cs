using Microsoft.Extensions.Options;

using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Workers;

public class RealtimePollingOptions
{
    public int IntervalSeconds { get; set; } = 30;
    public int MaxConsecutiveFailuresBeforeDeactivate { get; set; } = 10;
    public int VehicleStaleCutoffMinutes { get; set; } = 5;
    public int InitialDelaySeconds { get; set; } = 10;
}

public class RealtimePollingWorker : BackgroundService
{
    private readonly ILogger<RealtimePollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RealtimePollingOptions> _options;

    public RealtimePollingWorker(ILogger<RealtimePollingWorker> logger, IServiceScopeFactory scopeFactory, IOptionsMonitor<RealtimePollingOptions> options) { _logger = logger; _scopeFactory = scopeFactory; _options = options; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Realtime polling worker started with {Interval}s interval", _options.CurrentValue.IntervalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.InitialDelaySeconds), ct);

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

            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), ct);
        }
    }
}
