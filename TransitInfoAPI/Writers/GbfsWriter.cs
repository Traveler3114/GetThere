using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Writers;

public class GbfsWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GbfsWriter> _logger;

    public GbfsWriter(IServiceScopeFactory scopeFactory, ILogger<GbfsWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> WriteAsync(List<Dictionary<string, object?>> records, int? mobilityProviderId, CancellationToken ct)
    {
        if (records.Count == 0) return 0;

        if (mobilityProviderId is null)
        {
            _logger.LogWarning("GBFS writer: no MobilityProviderId configured for this feed");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var mobilityManager = scope.ServiceProvider.GetRequiredService<MobilityManager>();
        var written = await mobilityManager.UpsertStationsFromCustomFeedAsync(mobilityProviderId.Value, records, ct);
        _logger.LogInformation("GbfsWriter: upserted {Count} stations", written);
        return written;
    }
}
