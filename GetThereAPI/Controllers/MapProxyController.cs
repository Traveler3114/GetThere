using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

using GetThereAPI.Managers;
using GetThereShared.Contracts;
using GetThereAPI.Common;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("api/map")]
[Authorize]
public class MapProxyController : ControllerBase
{
    private readonly MapManager _mapManager;

public MapProxyController(MapManager mapManager) { _mapManager = mapManager; }

    [HttpGet("stations")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<List<MapStationResponse>>> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var stations = await _mapManager.GetStationsAsync(lat, lon, radiusKm, ct);
        return Ok(stations);
    }

    [HttpGet("routes")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<List<MapRouteResponse>>> GetRoutes(
        [FromQuery] int? operatorId,
        [FromQuery] string? routeType,
        CancellationToken ct = default)
    {
        var routes = await _mapManager.GetRoutesAsync(operatorId, routeType, ct);
        return Ok(routes);
    }

    [HttpGet("mobility/stations")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<List<MapMobilityStationResponse>>> GetMobilityStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var stations = await _mapManager.GetMobilityStationsAsync(lat, lon, radiusKm, ct);
        return Ok(stations);
    }

    [HttpGet("vehicles")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<List<MapVehicleResponse>>> GetVehicles(
        [FromQuery] string? feedId,
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var vehicles = await _mapManager.GetVehiclesAsync(feedId, lat, lon, radiusKm, ct);
        return Ok(vehicles);
    }

    [HttpGet("departures/{onestopId}")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<List<MapDepartureResponse>>> GetDepartures(string onestopId, CancellationToken ct = default)
    {
        var departures = await _mapManager.GetDeparturesAsync(onestopId, ct);
        return Ok(departures);
    }

    [HttpGet("operators/station/{onestopId}")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<List<MapOperatorResponse>>> GetStationOperators(string onestopId, CancellationToken ct = default)
    {
        var operators = await _mapManager.GetStationOperatorsAsync(onestopId, ct);
        return Ok(operators);
    }

    [HttpGet("transport-types")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<JsonElement>> GetTransportTypes(CancellationToken ct = default)
    {
        var types = await _mapManager.GetTransportTypesAsync(ct);
        return Ok(types);
    }
}