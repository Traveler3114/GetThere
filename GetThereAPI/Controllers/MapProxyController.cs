using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("api/map")]
[Authorize]
public class MapProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppDbContext _db;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MapProxyController(IHttpClientFactory httpClientFactory, AppDbContext db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var client = _httpClientFactory.CreateClient("TransitInfoApi");
        var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is not null)
            client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(authHeader);
        return client;
    }

    [HttpGet("stations")]
    public async Task<ActionResult> GetStations(
        [FromQuery] double? lat, [FromQuery] double? lon,
        [FromQuery] double? radiusKm, [FromQuery] int? countryId,
        CancellationToken ct = default)
    {
        var client = await CreateClientAsync();
        var query = BuildQuery(("lat", lat), ("lon", lon), ("radiusKm", radiusKm), ("countryId", countryId));
        var response = await client.GetAsync($"/stations{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var stations = await ExtractDataArrayAsync(response, ct);
        if (stations is null) return Ok(new List<MapStationResponse>());

        var result = stations.Select(s => new MapStationResponse
        {
            Id = s.GetProperty("id").GetInt32(),
            OnestopId = s.GetProperty("onestopId").GetString() ?? string.Empty,
            Name = s.GetProperty("name").GetString() ?? string.Empty,
            Latitude = s.GetProperty("latitude").GetDouble(),
            Longitude = s.GetProperty("longitude").GetDouble(),
            StationType = s.TryGetProperty("stationType", out var st) ? st.GetString() : null
        }).ToList();

        return Ok(result);
    }

    [HttpGet("routes")]
    public async Task<ActionResult> GetRoutes(
        [FromQuery] int? operatorId, [FromQuery] string? routeType,
        CancellationToken ct = default)
    {
        var client = await CreateClientAsync();
        var query = BuildQuery(("operatorId", operatorId), ("routeType", routeType));
        var response = await client.GetAsync($"/routes{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var routes = await ExtractDataArrayAsync(response, ct);
        if (routes is null) return Ok(new List<MapRouteResponse>());

        var result = routes.Select(r => new MapRouteResponse
        {
            Id = r.GetProperty("id").GetInt32(),
            OnestopId = r.GetProperty("onestopId").GetString() ?? string.Empty,
            Name = r.GetProperty("name").GetString() ?? string.Empty,
            RouteType = r.TryGetProperty("routeType", out var rt) ? rt.GetString() : null,
            OperatorName = string.Empty
        }).ToList();

        return Ok(result);
    }

    [HttpGet("mobility/stations")]
    public async Task<ActionResult> GetMobilityStations(
        [FromQuery] double? lat, [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var client = await CreateClientAsync();
        var query = BuildQuery(("lat", lat), ("lon", lon), ("radiusKm", radiusKm));
        var response = await client.GetAsync($"/mobility/stations{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var stations = await ExtractDataArrayAsync(response, ct);
        if (stations is null) return Ok(new List<MapMobilityStationResponse>());

        var result = stations.Select(s => new MapMobilityStationResponse
        {
            StationId = s.GetProperty("stationId").GetString() ?? string.Empty,
            Name = s.GetProperty("name").GetString() ?? string.Empty,
            Latitude = s.GetProperty("latitude").GetDouble(),
            Longitude = s.GetProperty("longitude").GetDouble(),
            AvailableVehicles = s.GetProperty("availableVehicles").GetInt32(),
            Capacity = s.GetProperty("capacity").GetInt32(),
            ProviderName = s.GetProperty("providerName").GetString() ?? string.Empty
        }).ToList();

        return Ok(result);
    }

    [HttpGet("realtime/vehicles")]
    public async Task<ActionResult> GetVehicles(
        [FromQuery] string? feedId, [FromQuery] double? lat,
        [FromQuery] double? lon, [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var client = await CreateClientAsync();
        double? minLat = null, minLon = null, maxLat = null, maxLon = null;
        if (lat.HasValue && lon.HasValue && radiusKm.HasValue)
        {
            var latDeg = radiusKm.Value / 111.0;
            var lonDeg = radiusKm.Value / (111.0 * Math.Cos(lat.Value * Math.PI / 180));
            minLat = lat.Value - latDeg;
            maxLat = lat.Value + latDeg;
            minLon = lon.Value - lonDeg;
            maxLon = lon.Value + lonDeg;
        }
        var query = BuildQuery(("feedId", feedId), ("minLat", minLat), ("minLon", minLon), ("maxLat", maxLat), ("maxLon", maxLon));
        var response = await client.GetAsync($"/realtime/vehicles{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var vehicles = await ExtractDataArrayAsync(response, ct);
        if (vehicles is null) return Ok(new List<MapVehicleResponse>());

        var result = vehicles.Select(v => new MapVehicleResponse
        {
            VehicleId = v.GetProperty("vehicleId").GetString() ?? string.Empty,
            RouteId = v.TryGetProperty("routeId", out var ri) ? ri.GetString() : null,
            TripId = v.TryGetProperty("tripId", out var ti) ? ti.GetString() : null,
            RouteShortName = v.TryGetProperty("routeShortName", out var rsn) ? rsn.GetString() : null,
            IsRealtime = v.TryGetProperty("isRealtime", out var irt) && irt.GetBoolean(),
            BlockId = v.TryGetProperty("blockId", out var bi) ? bi.GetString() : null,
            Latitude = v.GetProperty("latitude").GetDouble(),
            Longitude = v.GetProperty("longitude").GetDouble(),
            Bearing = v.TryGetProperty("bearing", out var be) ? be.GetDouble() : null,
            LastUpdated = v.TryGetProperty("lastUpdated", out var lu) ? lu.GetDateTime() : null
        }).ToList();

        return Ok(result);
    }

    [HttpGet("stations/{onestopId}/departures")]
    public async Task<ActionResult> GetDepartures(
        string onestopId, CancellationToken ct = default)
    {
        var client = await CreateClientAsync();

        var stationResponse = await client.GetAsync($"/stations/by-onestop/{onestopId}", ct);
        if (!stationResponse.IsSuccessStatusCode)
            return StatusCode((int)stationResponse.StatusCode);

        int stationId;
        {
            using var doc = await JsonDocument.ParseAsync(await stationResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            stationId = doc.RootElement.GetProperty("id").GetInt32();
        }

        var response = await client.GetAsync($"/stations/{stationId}/departures", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var departures = await ExtractDataArrayAsync(response, ct);
        if (departures is null) return Ok(new List<MapDepartureResponse>());

        var result = departures.Select(d => new MapDepartureResponse
        {
            TripId = d.GetProperty("tripId").GetString() ?? string.Empty,
            RouteName = d.GetProperty("routeName").GetString() ?? string.Empty,
            Headsign = d.TryGetProperty("headsign", out var h) ? h.GetString() ?? string.Empty : string.Empty,
            ScheduledDeparture = d.TryGetProperty("scheduledDeparture", out var sd) ? sd.GetDateTime() : null,
            EstimatedDeparture = d.TryGetProperty("estimatedDeparture", out var ed) ? ed.GetDateTime() : null,
            DelaySeconds = d.TryGetProperty("delaySeconds", out var ds) ? ds.GetInt32() : null
        }).ToList();

        return Ok(result);
    }

    [HttpGet("stations/{onestopId}/operators")]
    public async Task<ActionResult> GetStationOperators(
        string onestopId, CancellationToken ct = default)
    {
        var client = await CreateClientAsync();

        var stationResponse = await client.GetAsync($"/stations/by-onestop/{onestopId}", ct);
        if (!stationResponse.IsSuccessStatusCode)
            return StatusCode((int)stationResponse.StatusCode);

        int stationId;
        {
            using var doc = await JsonDocument.ParseAsync(await stationResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            stationId = doc.RootElement.GetProperty("id").GetInt32();
        }

        var response = await client.GetAsync($"/stations/{stationId}/operators", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var operators = await ExtractDataArrayAsync(response, ct);
        if (operators is null) return Ok(new List<MapOperatorResponse>());

        List<MapOperatorResponse> result = [];
        foreach (var op in operators)
        {
            var opGlobalId = op.GetProperty("globalId").GetString() ?? string.Empty;
            var hasTicketing = await _db.TicketingAdapters
                .AnyAsync(a => a.TransitInfoGlobalId == opGlobalId && a.IsActive, ct);

            result.Add(new MapOperatorResponse
            {
                GlobalId = opGlobalId,
                Name = op.GetProperty("name").GetString() ?? string.Empty,
                OperatorType = op.TryGetProperty("operatorType", out var ot) ? ot.GetString() ?? string.Empty : string.Empty,
                HasTicketing = hasTicketing
            });
        }

        return Ok(result);
    }

    [HttpGet("operators/types")]
    public async Task<ActionResult> GetTransportTypes(CancellationToken ct = default)
    {
        var client = await CreateClientAsync();
        var response = await client.GetAsync("/operators/types", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return Ok(doc.RootElement.Clone());
    }

    private static async Task<List<JsonElement>?> ExtractDataArrayAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<List<JsonElement>>(doc.RootElement.GetRawText(), JsonOptions);
        return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<JsonElement>>(data.GetRawText(), JsonOptions)
            : null;
    }

    private static string BuildQuery(params (string key, object? value)[] parameters)
    {
        var parts = parameters
            .Where(p => p.value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value!.ToString()!)}");
        var joined = string.Join("&", parts);
        return string.IsNullOrEmpty(joined) ? "" : "?" + joined;
    }
}
