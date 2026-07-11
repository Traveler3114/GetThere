using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("api/map")]
[Authorize]
public class MapProxyController : ControllerBase
{
    private readonly MapManager _mapManager;

public MapProxyController(MapManager mapManager) { _mapManager = mapManager; }

    [HttpGet("stations")]
    public async Task<ActionResult<List<MapStationResponse>>> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var stations = await _mapManager.GetStationsAsync(lat, lon, radiusKm, null, HttpContext, ct);
        return Ok(stations);
    }

    [HttpGet("routes")]
    public async Task<ActionResult<List<MapRouteResponse>>> GetRoutes(
        [FromQuery] int? operatorId,
        [FromQuery] string? routeType,
        CancellationToken ct = default)
    {
        var routes = await _mapManager.GetRoutesAsync(operatorId, routeType, HttpContext, ct);
        return Ok(routes);
    }

    [HttpGet("mobility/stations")]
    public async Task<ActionResult<List<MapMobilityStationResponse>>> GetMobilityStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var stations = await _mapManager.GetMobilityStationsAsync(lat, lon, radiusKm, HttpContext, ct);
        return Ok(stations);
    }

    [HttpGet("vehicles")]
    public async Task<ActionResult<List<MapVehicleResponse>>> GetVehicles(
        [FromQuery] string? feedId,
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var vehicles = await _mapManager.GetVehiclesAsync(feedId, lat, lon, radiusKm, HttpContext, ct);
        return Ok(vehicles);
    }

    [HttpGet("departures/{onestopId}")]
    public async Task<ActionResult<List<MapDepartureResponse>>> GetDepartures(string onestopId, CancellationToken ct = default)
    {
        var departures = await _mapManager.GetDeparturesAsync(onestopId, HttpContext, ct);
        return Ok(departures);
    }

    [HttpGet("operators/station/{onestopId}")]
    public async Task<ActionResult<List<MapOperatorResponse>>> GetStationOperators(string onestopId, CancellationToken ct = default)
    {
        var operators = await _mapManager.GetStationOperatorsAsync(onestopId, HttpContext, ct);
        return Ok(operators);
    }

    [HttpGet("transport-types")]
    public async Task<ActionResult<JsonElement>> GetTransportTypes(CancellationToken ct = default)
    {
        var types = await _mapManager.GetTransportTypesAsync(HttpContext, ct);
        return Ok(types);
    }
}