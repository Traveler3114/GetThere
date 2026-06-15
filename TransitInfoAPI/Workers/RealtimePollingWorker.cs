using TransitInfoAPI.Services;

namespace TransitInfoAPI.Workers;

public class RealtimePollingWorker : BackgroundService
{
    private readonly ILogger<RealtimePollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public RealtimePollingWorker(
        ILogger<RealtimePollingWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Realtime polling worker started with 30s interval");

        // Initial delay to let the app settle
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var realtime = scope.ServiceProvider.GetRequiredService<RealtimeService>();
                await realtime.PollAllFeedsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during realtime polling cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
