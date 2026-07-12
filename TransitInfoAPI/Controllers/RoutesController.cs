using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class RoutesController : ControllerBase
{
    private readonly RouteManager _routeManager;
    private readonly ScheduleManager _scheduleManager;

public RoutesController(RouteManager routeManager, ScheduleManager scheduleManager) { _routeManager = routeManager; _scheduleManager = scheduleManager; }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> GetAll(
        [FromQuery] int? operatorId,
        [FromQuery] RouteType? routeType,
        [FromQuery] double? minLat,
        [FromQuery] double? minLon,
        [FromQuery] double? maxLat,
        [FromQuery] double? maxLon,
        [FromQuery] string? q = null,
        [FromQuery] string? format = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        if (format == "geojson")
        {
            var fc = await _routeManager.GetAllGeoJsonAsync(operatorId, routeType, minLat, minLon, maxLat, maxLon, 5000, ct);
            return Ok(fc);
        }

        var result = await _routeManager.GetAllAsync(operatorId, routeType, q, page, perPage, ct);
        var total = await _routeManager.GetTotalCountAsync(operatorId, routeType, q, ct);
        return Ok(new Paginated<RouteResponse>(result, total, page, perPage));
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<RouteResponse>> GetById(int id, CancellationToken ct = default)
    {
        var route = await _routeManager.GetByIdAsync(id, ct);
        if (route is null) return NotFound();
        return Ok(route);
    }

    [HttpGet("by-onestop/{onestopId}")]
    [Authorize(Policy = PermissionKeys.RoutesView)]
    public async Task<ActionResult<RouteResponse>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var route = await _routeManager.GetByOnestopIdAsync(onestopId, ct);
        if (route is null) return NotFound();
        return Ok(route);
    }

    [HttpGet("{id}/shape")]
    [Authorize(Policy = PermissionKeys.RoutesView)]
    public async Task<ActionResult> GetShape(int id, CancellationToken ct = default)
    {
        var geometry = await _routeManager.GetShapeGeometryAsync(id, ct);
        if (geometry is null)
            return NotFound();

        return Ok(new
        {
            type = "Feature",
            geometry = new
            {
                type = geometry.GeometryType,
                coordinates = geometry.Coordinates.Select(c => new[] { c.X, c.Y })
            },
            properties = new { }
        });
    }

    [Authorize(Policy = PermissionKeys.RoutesManage)]
    [HttpPut("{id}/shape")]
    public async Task<ActionResult> UpdateShape(int id, [FromBody] GeoJsonLineStringGeometry body, CancellationToken ct = default)
    {
        var dto = await _routeManager.UpdateShapeAsync(id, body, ct);
        if (dto is null) return NotFound();
        return NoContent();
    }

    [HttpGet("{id}/stops")]
    [Authorize(Policy = PermissionKeys.RoutesView)]
    public async Task<ActionResult<List<StationResponse>>> GetStops(int id, CancellationToken ct = default)
    {
        var stops = await _scheduleManager.GetRouteStopsAsync(id, ct);
        return Ok(stops);
    }

    [HttpGet("{id}/trips")]
    [Authorize(Policy = PermissionKeys.RoutesView)]
    public async Task<ActionResult<List<TripResponse>>> GetTrips(
        int id,
        [FromQuery] string? date = null,
        CancellationToken ct = default)
    {
        var parsedDate = date is not null && DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);
        var trips = await _scheduleManager.GetRouteTripsAsync(id, parsedDate, ct);
        return Ok(trips);
    }
}