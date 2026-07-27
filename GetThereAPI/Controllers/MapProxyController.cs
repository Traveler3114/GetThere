using System.Text.Json;

using GetThereAPI.Common;
using GetThereAPI.Managers;

using GetThereShared.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Verbatim passthrough to a whitelisted TransitInfoAPI read.
    /// <para>
    /// The map page renders upstream shapes directly — GeoJSON feature collections in particular —
    /// so it needs the response unmodelled. This is what lets the client honour the one-way rule and
    /// talk only to GetThereAPI. The whitelist lives in
    /// <see cref="MapManager.IsAllowedUpstreamPath"/>; without it this would be an open gateway to
    /// TransitInfoAPI carrying the service account's credentials.
    /// </para>
    /// </summary>
    [HttpGet("upstream/{**path}")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<IActionResult> GetUpstream(string path, CancellationToken ct = default)
    {
        var (body, contentType) = await _mapManager.GetUpstreamAsync(path, Request.QueryString.Value, ct);
        return Content(body, contentType);
    }
}
