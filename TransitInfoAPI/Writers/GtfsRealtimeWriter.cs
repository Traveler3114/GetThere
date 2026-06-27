using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Writers;

public class GtfsRealtimeWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GtfsRealtimeWriter> _logger;

    public GtfsRealtimeWriter(IServiceScopeFactory scopeFactory, ILogger<GtfsRealtimeWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> WriteAsync(List<Dictionary<string, object?>> records, CancellationToken ct, string? feedIdOverride = null)
    {
        if (records.Count == 0) return 0;

        // Detect if these are vehicle positions or trip updates based on available fields
        var first = records[0];
        bool isVehicles = first.ContainsKey("vehicleId") || first.ContainsKey("vehicle_id");
        bool isTripUpdates = first.ContainsKey("tripId") || first.ContainsKey("trip_id");

        if (!isVehicles && !isTripUpdates)
        {
            _logger.LogWarning("GtfsRealtime records missing required fields — need vehicleId/lat/lng or tripId/stopSequence/delaySeconds");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var realtimeManager = scope.ServiceProvider.GetRequiredService<RealtimeManager>();

        if (isVehicles)
        {
            int count = 0;
            foreach (var record in records)
            {
                var vehicleId = GetString(record, "vehicleId") ?? GetString(record, "vehicle_id");
                var feedId = feedIdOverride ?? GetString(record, "feedId") ?? GetString(record, "feed_id") ?? "custom";
                var routeId = GetString(record, "routeId") ?? GetString(record, "route_id");
                var tripId = GetString(record, "tripId") ?? GetString(record, "trip_id");
                var lat = GetDouble(record, "latitude") ?? GetDouble(record, "lat");
                var lng = GetDouble(record, "longitude") ?? GetDouble(record, "lng") ?? GetDouble(record, "lon");
                var bearing = GetDouble(record, "bearing");

                if (vehicleId is null || lat is null || lng is null)
                    continue;

                var response = new Contracts.VehicleResponse
                {
                    VehicleId = vehicleId,
                    FeedId = feedId,
                    RouteId = routeId,
                    TripId = tripId,
                    Latitude = lat.Value,
                    Longitude = lng.Value,
                    Bearing = bearing,
                    LastUpdated = DateTime.UtcNow,
                    IsRealtime = true
                };

                realtimeManager.UpdateVehicleCache(feedId, response);
                count++;
            }

            _logger.LogInformation("GtfsRealtimeWriter: updated {Count} vehicle positions", count);
            return count;
        }

        if (isTripUpdates)
        {
            int count = 0;
            foreach (var record in records)
            {
                var tripId = GetString(record, "tripId") ?? GetString(record, "trip_id");
                var stopSequence = GetInt(record, "stopSequence") ?? GetInt(record, "stop_sequence");
                var delaySeconds = GetInt(record, "delaySeconds") ?? GetInt(record, "delay_seconds");

                if (tripId is null || stopSequence is null)
                    continue;

                realtimeManager.UpdateTripUpdate(tripId, stopSequence.Value, delaySeconds);
                count++;
            }

            _logger.LogInformation("GtfsRealtimeWriter: updated {Count} trip updates", count);
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
        if (double.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static int? GetInt(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (int.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }
}
