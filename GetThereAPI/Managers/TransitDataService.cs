using GetThereAPI.Data;
using GetThereAPI.Transit;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class TransitDataService
{
    private readonly AppDbContext _db;
    private readonly TransitOrchestrator _transit;

    public TransitDataService(AppDbContext db, TransitOrchestrator transit)
    {
        _db = db;
        _transit = transit;
    }

    private static string BuildOtpFeedId(int operatorId) => $"op{operatorId}";

    public async Task<List<StopDto>> GetAllStopsAsync(int? countryId = null, CancellationToken ct = default)
    {
        var stops = await _transit.GetStopsAsync(countryId, ct);

        if (!countryId.HasValue)
            return stops;

        var feedPrefixes = await GetFeedPrefixesForCountryAsync(countryId.Value, ct);
        if (feedPrefixes.Count == 0)
            return stops;

        return stops
            .Where(s => feedPrefixes.Any(prefix =>
                s.StopId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<List<RouteDto>> GetAllRoutesAsync(int? countryId = null, CancellationToken ct = default)
    {
        var routes = await _transit.GetRoutesAsync(countryId, ct);

        if (!countryId.HasValue)
            return routes;

        var feedPrefixes = await GetFeedPrefixesForCountryAsync(countryId.Value, ct);
        if (feedPrefixes.Count == 0)
            return routes;

        return routes
            .Where(r => feedPrefixes.Any(prefix =>
                r.RouteId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public Task<StopScheduleDto?> GetStopScheduleAsync(
        string stopId,
        int? countryId = null,
        CancellationToken ct = default)
        => _transit.GetStopScheduleAsync(countryId, stopId, ct);

    public Task<bool> IsTransitHealthyAsync(int? countryId = null, CancellationToken ct = default)
        => _transit.HealthCheckAsync(countryId, ct);

    private async Task<List<string>> GetFeedPrefixesForCountryAsync(int countryId, CancellationToken ct)
    {
        var operators = await _db.TransitOperators
            .Where(o => o.CountryId == countryId && o.GtfsFeedUrl != null)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync(ct);

        var prefixes = new List<string>();
        foreach (var op in operators)
        {
            if (!string.IsNullOrWhiteSpace(op.Name))
                prefixes.Add($"{op.Name.ToLowerInvariant()}:");

            prefixes.Add($"{BuildOtpFeedId(op.Id)}:");
        }

        return prefixes;
    }
}
