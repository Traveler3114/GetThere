using System.Text.Json;

using TransitInfoAPI.Models;

namespace TransitInfoAPI.Services;

public class RealtimeService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RealtimeService> _logger;

    public RealtimeService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<RealtimeService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<List<VehicleDto>> GetVehiclesAsync(string? operatorGlobalId, double? lat, double? lon, double? radiusKm, CancellationToken ct)
    {
        var otpBaseUrl = _config["Otp:InstanceBaseUrl"] ?? "http://localhost:8080";
        var http = _httpFactory.CreateClient("otp");

        try
        {
            var query = new OtpGraphQLQuery
            {
                query = @"
                {
                    vehicles {
                        vehicleId
                        trip {
                            tripId
                            route {
                                gtfsId
                                shortName
                            }
                        }
                        position {
                            latitude
                            longitude
                        }
                        bearing
                        lastUpdated
                    }
                }"
            };

            var response = await http.PostAsJsonAsync($"{otpBaseUrl}/otp/routers/default/index/graphql", query, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OTP GraphQL returned {Status}", response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

            var vehicles = new List<VehicleDto>();

            if (json.TryGetProperty("data", out var data) &&
                data.TryGetProperty("vehicles", out var vehiclesArray))
            {
                foreach (var item in vehiclesArray.EnumerateArray())
                {
                    try
                    {
                        var vehicleId = item.TryGetProperty("vehicleId", out var vid) ? vid.GetString() ?? string.Empty : string.Empty;
                        var tripId = item.TryGetProperty("trip", out var trip) && trip.TryGetProperty("tripId", out var tid) ? tid.GetString() : null;
                        var routeId = trip.TryGetProperty("route", out var route) && route.TryGetProperty("gtfsId", out var rid) ? rid.GetString() : null;
                        var latVal = item.GetProperty("position").GetProperty("latitude").GetDouble();
                        var lonVal = item.GetProperty("position").GetProperty("longitude").GetDouble();
                        var bearing = item.TryGetProperty("bearing", out var b) ? b.GetDouble() : (double?)null;
                        var lastUpdated = item.TryGetProperty("lastUpdated", out var lu) ? lu.GetDateTime() : (DateTime?)null;

                        if (lat is not null && lon is not null && radiusKm is not null)
                        {
                            var dist = CalculateDistance(lat.Value, lon.Value, latVal, lonVal);
                            if (dist > radiusKm.Value * 1000) continue;
                        }

                        vehicles.Add(new VehicleDto
                        {
                            VehicleId = vehicleId,
                            RouteId = routeId,
                            TripId = tripId,
                            Latitude = latVal,
                            Longitude = lonVal,
                            Bearing = bearing,
                            LastUpdated = lastUpdated
                        });
                    }
                    catch { }
                }
            }

            return vehicles;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch vehicle positions from OTP");
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

    private class OtpGraphQLQuery
    {
        public string query { get; set; } = string.Empty;
    }
}
