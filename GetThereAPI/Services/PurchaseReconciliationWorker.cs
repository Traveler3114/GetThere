using GetThereAPI.Managers;

namespace GetThereAPI.Services;

/// <summary>
/// Settles purchases that were paid for but never resolved.
/// <para>
/// The wallet debit commits before the operator is called, which is deliberate — no SQL transaction
/// is held open across an outbound HTTP request. The cost of that choice is a window in which the
/// process can die having taken the money and issued nothing. This worker is what closes that
/// window; without it a stranded purchase sits until a human spots it in the admin console.
/// </para>
/// </summary>
public class PurchaseReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PurchaseReconciliationWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _minimumAge;

    /// <summary>Floor on the poll interval — a configured 0 would spin against the database.</summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Floor on how long a purchase must be pending before it is treated as stranded. A value below
    /// this would let the sweep race adapter calls that are still legitimately in flight and refund
    /// a purchase out from under the caller.
    /// </summary>
    private static readonly TimeSpan MinimumAgeFloor = TimeSpan.FromMinutes(2);

    public PurchaseReconciliationWorker(
        IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PurchaseReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var configuredInterval = TimeSpan.FromMinutes(
            configuration.GetValue("PurchaseReconciliation:IntervalMinutes", 5));
        _interval = configuredInterval < MinimumInterval ? MinimumInterval : configuredInterval;

        var configuredAge = TimeSpan.FromMinutes(
            configuration.GetValue("PurchaseReconciliation:MinimumAgeMinutes", 5));
        _minimumAge = configuredAge < MinimumAgeFloor ? MinimumAgeFloor : configuredAge;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PurchaseReconciliationWorker started; sweeping every {Interval} for purchases pending longer than {MinimumAge}",
            _interval, _minimumAge);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Sweep before waiting: the most likely reason a purchase is stranded is that this
                // process died mid-purchase, so the restart that follows should settle it at once
                // rather than an interval later.
                using var scope = _scopeFactory.CreateScope();
                var ticketing = scope.ServiceProvider.GetRequiredService<TicketingManager>();

                var settled = await ticketing.ReconcilePendingPurchasesAsync(_minimumAge, stoppingToken);

                if (settled > 0)
                    _logger.LogInformation("Settled {Count} stranded purchase(s)", settled);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PurchaseReconciliationWorker");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PurchaseReconciliationWorker stopped");
    }
}
