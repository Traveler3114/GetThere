using GetThereAPI.Data;

using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Services;

public class TicketExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TicketExpiryWorker> _logger;
    private readonly TimeSpan _interval;

    public TicketExpiryWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<TicketExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromHours(configuration.GetValue<int>("TicketExpiry:CheckIntervalHours", 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TicketExpiryWorker started with interval {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var count = await db.ImportedTickets
                    .Where(t => t.Status == ImportedTicketStatus.Active && t.ValidTo < DateTime.UtcNow)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.Status, ImportedTicketStatus.Expired)
                        .SetProperty(t => t.UpdatedAt, DateTime.UtcNow),
                        stoppingToken);

                if (count > 0)
                    _logger.LogInformation("Marked {Count} imported tickets as expired", count);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TicketExpiryWorker");
            }
        }

        _logger.LogInformation("TicketExpiryWorker stopped");
    }
}
