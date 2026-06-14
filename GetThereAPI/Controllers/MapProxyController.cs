using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("api/map")]
public class MapProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MapProxyController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient CreateClient() =>
        _httpClientFactory.CreateClient("TransitInfoApi");

    private static async Task<List<JsonElement>?> ExtractDataAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<JsonElement>>(data.GetRawText(), JsonOptions)
            : null;
    }

    [HttpGet("stations")]
    public async Task<ActionResult<OperationResult<List<MapStationResponse>>>> GetStations(
        [FromQuery] double? lat, [FromQuery] double? lon,
        [FromQuery] double? radiusKm, [FromQuery] int? countryId,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        var query = BuildQuery(("lat", lat), ("lon", lon), ("radiusKm", radiusKm), ("countryId", countryId));
        var response = await client.GetAsync($"/stations{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, OperationResult<List<MapStationResponse>>.Fail("TransitInfoAPI error."));

        var stations = await ExtractDataAsync(response, ct);
        if (stations is null) return Ok(OperationResult<List<MapStationResponse>>.Ok([]));

        var result = stations.Select(s => new MapStationResponse
        {
            Id = s.GetProperty("id").GetInt32(),
            GlobalId = s.GetProperty("globalId").GetString() ?? string.Empty,
            Name = s.GetProperty("name").GetString() ?? string.Empty,
            Latitude = s.GetProperty("latitude").GetDouble(),
            Longitude = s.GetProperty("longitude").GetDouble(),
            StationType = s.TryGetProperty("stationType", out var st) ? st.GetString() : null
        }).ToList();

        return Ok(OperationResult<List<MapStationResponse>>.Ok(result));
    }

    [HttpGet("routes")]
    public async Task<ActionResult<OperationResult<List<MapRouteResponse>>>> GetRoutes(
        [FromQuery] int? operatorId, [FromQuery] string? routeType,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        var query = BuildQuery(("operatorId", operatorId), ("routeType", routeType));
        var response = await client.GetAsync($"/routes{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, OperationResult<List<MapRouteResponse>>.Fail("TransitInfoAPI error."));

        var routes = await ExtractDataAsync(response, ct);
        if (routes is null) return Ok(OperationResult<List<MapRouteResponse>>.Ok([]));

        var result = routes.Select(r => new MapRouteResponse
        {
            Id = r.GetProperty("id").GetInt32(),
            GlobalId = r.GetProperty("globalId").GetString() ?? string.Empty,
            Name = r.GetProperty("name").GetString() ?? string.Empty,
            RouteType = r.TryGetProperty("routeType", out var rt) ? rt.GetString() : null,
            OperatorName = string.Empty
        }).ToList();

        return Ok(OperationResult<List<MapRouteResponse>>.Ok(result));
    }

    [HttpGet("mobility/stations")]
    public async Task<ActionResult<OperationResult<List<MapMobilityStationResponse>>>> GetMobilityStations(
        [FromQuery] double? lat, [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        var query = BuildQuery(("lat", lat), ("lon", lon), ("radiusKm", radiusKm));
        var response = await client.GetAsync($"/mobility/stations{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, OperationResult<List<MapMobilityStationResponse>>.Fail("TransitInfoAPI error."));

        var stations = await ExtractDataAsync(response, ct);
        if (stations is null) return Ok(OperationResult<List<MapMobilityStationResponse>>.Ok([]));

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

        return Ok(OperationResult<List<MapMobilityStationResponse>>.Ok(result));
    }

    [HttpGet("realtime/vehicles")]
    public async Task<ActionResult<OperationResult<List<MapVehicleResponse>>>> GetVehicles(
        [FromQuery] string? operatorGlobalId, [FromQuery] double? lat,
        [FromQuery] double? lon, [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        var query = BuildQuery(("operatorGlobalId", operatorGlobalId), ("lat", lat), ("lon", lon), ("radiusKm", radiusKm));
        var response = await client.GetAsync($"/realtime/vehicles{query}", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, OperationResult<List<MapVehicleResponse>>.Fail("TransitInfoAPI error."));

        var vehicles = await ExtractDataAsync(response, ct);
        if (vehicles is null) return Ok(OperationResult<List<MapVehicleResponse>>.Ok([]));

        var result = vehicles.Select(v => new MapVehicleResponse
        {
            VehicleId = v.GetProperty("vehicleId").GetString() ?? string.Empty,
            RouteId = v.TryGetProperty("routeId", out var ri) ? ri.GetString() : null,
            TripId = v.TryGetProperty("tripId", out var ti) ? ti.GetString() : null,
            Latitude = v.GetProperty("latitude").GetDouble(),
            Longitude = v.GetProperty("longitude").GetDouble(),
            Bearing = v.TryGetProperty("bearing", out var be) ? be.GetDouble() : null,
            LastUpdated = v.TryGetProperty("lastUpdated", out var lu) ? lu.GetDateTime() : null
        }).ToList();

        return Ok(OperationResult<List<MapVehicleResponse>>.Ok(result));
    }

    [HttpGet("stations/{globalId}/departures")]
    public async Task<ActionResult<OperationResult<List<MapDepartureResponse>>>> GetDepartures(
        string globalId, CancellationToken ct = default)
    {
        var client = CreateClient();
        var response = await client.GetAsync($"/stations/{globalId}/departures", ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, OperationResult<List<MapDepartureResponse>>.Fail("TransitInfoAPI error."));

        var departures = await ExtractDataAsync(response, ct);
        if (departures is null) return Ok(OperationResult<List<MapDepartureResponse>>.Ok([]));

        var result = departures.Select(d => new MapDepartureResponse
        {
            TripId = d.GetProperty("tripId").GetString() ?? string.Empty,
            RouteName = d.GetProperty("routeName").GetString() ?? string.Empty,
            Headsign = d.TryGetProperty("headsign", out var h) ? h.GetString() ?? string.Empty : string.Empty,
            ScheduledDeparture = d.TryGetProperty("scheduledDeparture", out var sd) ? sd.GetDateTime() : null,
            EstimatedDeparture = d.TryGetProperty("estimatedDeparture", out var ed) ? ed.GetDateTime() : null,
            DelaySeconds = d.TryGetProperty("delaySeconds", out var ds) ? ds.GetInt32() : null
        }).ToList();

        return Ok(OperationResult<List<MapDepartureResponse>>.Ok(result));
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
