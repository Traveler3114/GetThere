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
            // Sweep first, then wait: delaying up front leaves tickets that expired while the
            // service was down showing as Active for a full interval after every restart.
            //
            // Each job is run and caught separately. They are unrelated to each other, and with one
            // try block around all four a failure in the first — a lock timeout on the imported
            // tickets update, say — silently skipped the other three for the whole interval, so
            // abandoned uploads accumulated on disk because of an unrelated fault.
            using (var scope = _scopeFactory.CreateScope())
            {
                var now = DateTime.UtcNow;

                if (!await RunJobAsync("expire imported tickets", async () =>
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var count = await db.ImportedTickets
                        .Where(t => t.Status == ImportedTicketStatus.Active && t.ValidTo < now)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(t => t.Status, ImportedTicketStatus.Expired)
                            .SetProperty(t => t.UpdatedAt, now),
                            stoppingToken);

                    if (count > 0)
                        _logger.LogInformation("Marked {Count} imported tickets as expired", count);
                })) break;

                // Purchased tickets expire too. This worker only swept imported ones, so a bought
                // ticket stayed Active forever once its validity window closed.
                if (!await RunJobAsync("expire purchased tickets", async () =>
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var count = await db.Tickets
                        .Where(t => t.Status == TicketStatus.Active && t.ValidTo != null && t.ValidTo < now)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(t => t.Status, TicketStatus.Expired),
                            stoppingToken);

                    if (count > 0)
                        _logger.LogInformation("Marked {Count} purchased tickets as expired", count);
                })) break;

                // Files uploaded but never turned into a ticket. Nothing else references them, so
                // without this sweep every abandoned import leaves a blob on disk permanently.
                if (!await RunJobAsync("purge abandoned uploads", async () =>
                {
                    var uploads = scope.ServiceProvider.GetRequiredService<Managers.TicketUploadManager>();
                    var purged = await uploads.PurgeAbandonedAsync(stoppingToken);

                    if (purged > 0)
                        _logger.LogInformation("Purged {Count} abandoned ticket uploads", purged);
                })) break;

                // Journey status is a function of its legs' dates, so it is rolled forward here
                // rather than set by hand — otherwise it drifts from the tickets it describes.
                if (!await RunJobAsync("roll up journey statuses", async () =>
                {
                    var journeys = scope.ServiceProvider.GetRequiredService<Managers.JourneyManager>();
                    var rolled = await journeys.RollUpStatusesAsync(stoppingToken);

                    if (rolled > 0)
                        _logger.LogInformation("Rolled {Count} journeys to a new status", rolled);
                })) break;
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

    /// <summary>
    /// Runs one sweep job, logging and swallowing its failure so the others still run.
    /// Returns <c>false</c> when shutdown was requested, which is the only reason to stop the loop.
    /// </summary>
    private async Task<bool> RunJobAsync(string jobName, Func<Task> job)
    {
        try
        {
            await job();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TicketExpiryWorker job {JobName} failed; the remaining jobs still ran", jobName);
            return true;
        }
    }
}
