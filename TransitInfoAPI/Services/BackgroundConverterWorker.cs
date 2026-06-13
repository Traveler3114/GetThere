using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Services.Converters;

namespace TransitInfoAPI.Services;

public class BackgroundConverterWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConverterRegistry _registry;
    private readonly ILogger<BackgroundConverterWorker> _logger;

    public BackgroundConverterWorker(
        IServiceScopeFactory scopeFactory,
        ConverterRegistry registry,
        ILogger<BackgroundConverterWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Converter worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPendingConversions(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Converter worker error");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task RunPendingConversions(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();

        var activeConverters = await db.FeedConverters
            .Include(fc => fc.Feed)
            .Where(fc => fc.IsActive)
            .ToListAsync(ct);

        foreach (var converterConfig in activeConverters)
        {
            var converter = _registry.Get(converterConfig.ConverterType);
            if (converter is null)
            {
                _logger.LogWarning("No converter registered for type {Type}", converterConfig.ConverterType);
                continue;
            }

            try
            {
                await converter.ConvertAsync(converterConfig, ct);
                converterConfig.LastRun = DateTime.UtcNow;
                converterConfig.LastSuccess = true;
                _logger.LogInformation("Converter {Type} for feed {FeedId} succeeded", converterConfig.ConverterType, converterConfig.Feed.FeedId);
            }
            catch (Exception ex)
            {
                converterConfig.LastRun = DateTime.UtcNow;
                converterConfig.LastSuccess = false;
                _logger.LogError(ex, "Converter {Type} for feed {FeedId} failed", converterConfig.ConverterType, converterConfig.Feed.FeedId);
            }
        }

        if (activeConverters.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
