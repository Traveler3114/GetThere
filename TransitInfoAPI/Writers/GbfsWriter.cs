using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using TransitInfoAPI.Contracts;
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

        if (records.Count == 0) return 0;

        var first = records[0];
        bool isStations = first.ContainsKey("stationId") || first.ContainsKey("station_id") || first.ContainsKey("uid");
        bool isFreeFloating = (first.ContainsKey("bikeId") || first.ContainsKey("bike_id") || first.ContainsKey("vehicleId"))
            && (first.ContainsKey("lat") || first.ContainsKey("latitude") || first.ContainsKey("lon") || first.ContainsKey("longitude"));

        if (!isStations && !isFreeFloating)
        {
            _logger.LogWarning("GbfsWriter: records missing recognizable fields — need stationId/station_id or bikeId/vehicleId + lat/lon");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();

        if (isStations)
        {
            if (mobilityProviderId is null)
            {
                _logger.LogWarning("GbfsWriter: station records require a MobilityProviderId");
                return 0;
            }

            var mobilityManager = scope.ServiceProvider.GetRequiredService<MobilityManager>();
            var written = await mobilityManager.UpsertStationsFromCustomFeedAsync(mobilityProviderId.Value, records, ct);
            _logger.LogInformation("GbfsWriter: wrote {Count} station records", written);
            return written;
        }

        if (isFreeFloating)
        {
            var realtimeManager = scope.ServiceProvider.GetRequiredService<RealtimeManager>();
            var feedId = mobilityProviderId is not null ? $"gbfs-custom-{mobilityProviderId}" : "gbfs-custom";
            int count = 0;

            foreach (var record in records)
            {
                var vehicleId = GetString(record, "bikeId") ?? GetString(record, "bike_id") ?? GetString(record, "vehicleId") ?? GetString(record, "vehicle_id");
                var lat = GetDouble(record, "lat") ?? GetDouble(record, "latitude");
                var lng = GetDouble(record, "lon") ?? GetDouble(record, "lng") ?? GetDouble(record, "longitude");

                if (vehicleId is null || lat is null || lng is null)
                    continue;

                realtimeManager.UpdateVehicleCache(feedId, new VehicleResponse
                {
                    VehicleId = vehicleId,
                    FeedId = feedId,
                    Latitude = lat.Value,
                    Longitude = lng.Value,
                    LastUpdated = DateTime.UtcNow,
                    IsRealtime = true
                });
                count++;
            }

            _logger.LogInformation("GbfsWriter: wrote {Count} free-floating vehicle records", count);
            return count;
        }

        return 0;
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        return dict.TryGetValue(key, out var v) ? v?.ToString() : null;
    }

    private static double? GetDouble(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is long l) return l;
        if (double.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }
}
