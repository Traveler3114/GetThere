using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Services;

public class StationService
{
    private readonly TransitDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<StationService> _logger;

    public StationService(
        TransitDbContext db,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<StationService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<List<StationDto>> GetAllAsync(double? lat, double? lon, double? radiusKm, int? countryId, CancellationToken ct)
    {
        var query = _db.CanonicalStations
            .Include(cs => cs.Country)
            .Where(cs => cs.IsActive)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / 111.0;
            var lonRange = radiusKm.Value / (111.0 * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(cs =>
                cs.Latitude >= lat.Value - latRange &&
                cs.Latitude <= lat.Value + latRange &&
                cs.Longitude >= lon.Value - lonRange &&
                cs.Longitude <= lon.Value + lonRange);
        }

        return await query.Select(cs => new StationDto
        {
            Id = cs.Id,
            GlobalId = cs.GlobalId,
            Name = cs.Name,
            Latitude = cs.Latitude,
            Longitude = cs.Longitude,
            StationType = cs.StationType.ToString(),
            CountryName = cs.Country.Name,
            CityName = cs.City != null ? cs.City.Name : null
        }).ToListAsync(ct);
    }

    public async Task<StationDto?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.CanonicalStations
            .Include(cs => cs.Country)
            .Include(cs => cs.City)
            .Where(cs => cs.GlobalId == globalId && cs.IsActive)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                CountryName = cs.Country.Name,
                CityName = cs.City != null ? cs.City.Name : null
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<StationOperatorDto>> GetOperatorsAsync(string globalId, CancellationToken ct)
    {
        var station = await _db.CanonicalStations
            .FirstOrDefaultAsync(cs => cs.GlobalId == globalId && cs.IsActive, ct);

        if (station is null) return [];

        return await _db.CanonicalStationOperators
            .Include(cso => cso.Operator)
            .Where(cso => cso.CanonicalStationId == station.Id)
            .Select(cso => new StationOperatorDto
            {
                GlobalId = cso.Operator.GlobalId,
                Name = cso.Operator.Name,
                OperatorType = cso.Operator.OperatorType.ToString(),
                PlatformInfo = cso.PlatformInfo
            })
            .ToListAsync(ct);
    }

    public async Task<List<DepartureDto>> GetDeparturesAsync(string globalId, CancellationToken ct)
    {
        var otpBaseUrl = _config["Otp:InstanceBaseUrl"] ?? "http://localhost:8080";
        var http = _httpFactory.CreateClient("otp");

        try
        {
            var query = new OtpDepartureQuery { query = $@"
                {{
                    stop(id: ""{globalId}"") {{
                        name
                        stoptimesWithoutPatterns(numberOfDepartures: 20) {{
                            trip {{
                                tripHeadsign
                                routeShortName
                                tripId
                            }}
                            scheduledDeparture
                            realtimeDeparture
                            realtime
                            serviceDay
                        }}
                    }}
                }}" };

            var response = await http.PostAsJsonAsync($"{otpBaseUrl}/otp/routers/default/index/graphql", query, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OTP GraphQL returned {Status}", response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var stoptimes = json.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("stop", out var stop) &&
                            stop.TryGetProperty("stoptimesWithoutPatterns", out var stoptimesArray)
                ? stoptimesArray.EnumerateArray().Select(ParseDeparture).Where(d => d is not null).ToList()
                : [];

            return stoptimes!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch departures from OTP for {GlobalId}", globalId);
            return [];
        }
    }

    private static DepartureDto? ParseDeparture(JsonElement item)
    {
        try
        {
            var trip = item.GetProperty("trip");
            var scheduledDeparture = item.TryGetProperty("scheduledDeparture", out var sd) ? sd.GetInt32() : 0;
            var realtimeDeparture = item.TryGetProperty("realtimeDeparture", out var rd) ? rd.GetInt32() : 0;
            var serviceDay = item.TryGetProperty("serviceDay", out var sday) ? sday.GetInt64() : 0;
            var isRealtime = item.TryGetProperty("realtime", out var rt) && rt.GetBoolean();

            var scheduledTime = DateTimeOffset.FromUnixTimeSeconds(serviceDay + scheduledDeparture).DateTime;
            DateTime? estimatedTime = isRealtime
                ? DateTimeOffset.FromUnixTimeSeconds(serviceDay + realtimeDeparture).DateTime
                : null;

            return new DepartureDto
            {
                TripId = trip.TryGetProperty("tripId", out var tid) ? tid.GetString() ?? string.Empty : string.Empty,
                RouteName = trip.TryGetProperty("routeShortName", out var rsn) ? rsn.GetString() ?? string.Empty : string.Empty,
                Headsign = trip.TryGetProperty("tripHeadsign", out var th) ? th.GetString() ?? string.Empty : string.Empty,
                ScheduledDeparture = scheduledTime,
                EstimatedDeparture = estimatedTime,
                DelaySeconds = isRealtime ? realtimeDeparture - scheduledDeparture : null
            };
        }
        catch
        {
            return null;
        }
    }

    private class OtpDepartureQuery
    {
        public string query { get; set; } = string.Empty;
    }
}
