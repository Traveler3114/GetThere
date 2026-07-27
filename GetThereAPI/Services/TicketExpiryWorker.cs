using GetThereAPI.Data;

using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Services;

public class TicketExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TicketExpiryWorker> _logger;
    private readonly TimeSpan _interval;

    /// <summary>Floor on the poll interval — a configured 0 would otherwise spin against the database.</summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    public TicketExpiryWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<TicketExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var configured = TimeSpan.FromHours(configuration.GetValue("TicketExpiry:CheckIntervalHours", 1));
        _interval = configured < MinimumInterval ? MinimumInterval : configured;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TicketExpiryWorker started with interval {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Sweep first, then wait: delaying up front leaves tickets that expired while the
                // service was down showing as Active for a full interval after every restart.
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;

                var importedCount = await db.ImportedTickets
                    .Where(t => t.Status == ImportedTicketStatus.Active && t.ValidTo < now)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.Status, ImportedTicketStatus.Expired)
                        .SetProperty(t => t.UpdatedAt, now),
                        stoppingToken);

                if (importedCount > 0)
                    _logger.LogInformation("Marked {Count} imported tickets as expired", importedCount);

                // Purchased tickets expire too. This worker only swept imported ones, so a bought
                // ticket stayed Active forever once its validity window closed.
                var purchasedCount = await db.Tickets
                    .Where(t => t.Status == TicketStatus.Active && t.ValidTo != null && t.ValidTo < now)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.Status, TicketStatus.Expired),
                        stoppingToken);

                if (purchasedCount > 0)
                    _logger.LogInformation("Marked {Count} purchased tickets as expired", purchasedCount);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TicketExpiryWorker");
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

        _logger.LogInformation("TicketExpiryWorker stopped");
    }
}
