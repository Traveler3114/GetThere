using TransitInfoAPI.Models;

using transit_realtime;

namespace TransitInfoAPI.Services;

public class RealtimeService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RealtimeService> _logger;

    public RealtimeService(
        IHttpClientFactory httpFactory,
        ILogger<RealtimeService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<List<VehicleDto>> GetVehiclesAsync(string? operatorGlobalId, double? lat, double? lon, double? radiusKm, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("gtfsrt");

        try
        {
            var response = await http.GetAsync("https://www.zet.hr/gtfs-rt-protobuf", ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsByteArrayAsync(ct);
            var feed = FeedMessage.Parser.ParseFrom(body);

            _logger.LogInformation("GTFS-RT feed: {Count} entities, {Vp} vehicle positions", feed.Entity.Count,
                feed.Entity.Count(e => e.VehiclePosition is not null));

            var vehicles = new List<VehicleDto>();

            foreach (var entity in feed.Entity)
            {
                var vp = entity.VehiclePosition;
                if (vp is null || vp.Position is null) continue;

                var tripId = vp.Trip?.TripId;
                var routeId = vp.Trip?.RouteId;
                var vehicleId = vp.Vehicle?.Id ?? entity.Id;

                if (lat is not null && lon is not null && radiusKm is not null)
                {
                    var dist = CalculateDistance(lat.Value, lon.Value, vp.Position.Latitude, vp.Position.Longitude);
                    if (dist > radiusKm.Value * 1000) continue;
                }

                var lastUpdated = vp.Timestamp > 0
                    ? DateTime.UnixEpoch.AddSeconds(vp.Timestamp)
                    : (DateTime?)null;

                vehicles.Add(new VehicleDto
                {
                    VehicleId = vehicleId,
                    RouteId = string.IsNullOrEmpty(routeId) ? null : $"zet:{routeId}",
                    TripId = tripId,
                    RouteShortName = null,
                    IsRealtime = true,
                    BlockId = null,
                    Latitude = vp.Position.Latitude,
                    Longitude = vp.Position.Longitude,
                    Bearing = vp.Position.Bearing > 0 ? vp.Position.Bearing : null,
                    LastUpdated = lastUpdated
                });
            }

            return vehicles;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch vehicle positions from GTFS-RT");
            return [];
        }
    }

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }
}

