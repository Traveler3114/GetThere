using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Geometries;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class RoutesController : ControllerBase
{
    private readonly RouteManager _routeManager;
    private readonly ScheduleManager _scheduleManager;
    private readonly TransitDbContext _db;

    public RoutesController(RouteManager routeManager, ScheduleManager scheduleManager, TransitDbContext db)
    {
        _routeManager = routeManager;
        _scheduleManager = scheduleManager;
        _db = db;
    }

    [HttpGet]
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
            var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable();

            if (operatorId.HasValue)
                query = query.Where(r => r.OperatorId == operatorId.Value);
            if (routeType.HasValue)
                query = query.Where(r => r.RouteType == routeType.Value);

            if (minLat.HasValue && minLon.HasValue && maxLat.HasValue && maxLon.HasValue)
            {
                var envelope = new Envelope(minLon.Value, maxLon.Value, minLat.Value, maxLat.Value);
                var gf = new NetTopologySuite.Geometries.GeometryFactory(new NetTopologySuite.Geometries.PrecisionModel(), 4326);
                var bbox = gf.ToGeometry(envelope);
                if (bbox is Polygon poly && !NetTopologySuite.Algorithm.Orientation.IsCCW(poly.Shell.Coordinates))
                    bbox = poly.Reverse();
                query = query.Where(r => r.Geometry != null && r.Geometry.Intersects(bbox));
            }

            var routes = await query.OrderBy(r => r.Id).Take(5000).ToListAsync(ct);

            var fc = GeoJsonGeometry.ToLineStringCollection(routes,
                r => r.Geometry,
                r => new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["onestopId"] = r.OnestopId,
                    ["name"] = r.LongName,
                    ["shortName"] = r.ShortName,
                    ["routeType"] = r.RouteType.ToString(),
                    ["operatorId"] = r.OperatorId,
                    ["shapeEdited"] = r.ShapeEdited
                });
            return Ok(fc);
        }

        var result = await _routeManager.GetAllAsync(operatorId, routeType, q, page, perPage, ct);
        var total = await _db.CanonicalRoutes.CountAsync(r => r.IsActive, ct);
        return Ok(new Paginated<RouteResponse>(result, total, page, perPage));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RouteResponse>> GetById(int id, CancellationToken ct = default)
    {
        var route = await _db.CanonicalRoutes
            .Where(r => r.Id == id)
            .Select(RouteMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);

        if (route is null)
            return NotFound();

        return Ok(route);
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<RouteResponse>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var route = await _db.CanonicalRoutes
            .Where(r => r.OnestopId == onestopId)
            .Select(RouteMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);

        if (route is null)
            return NotFound();

        return Ok(route);
    }

    [HttpGet("{id}/shape")]
    public async Task<ActionResult> GetShape(int id, CancellationToken ct = default)
    {
        var geometry = await _db.CanonicalRoutes
            .Where(r => r.Id == id)
            .Select(r => r.Geometry)
            .FirstOrDefaultAsync(ct);

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

    [HttpPut("{id}/shape")]
    public async Task<ActionResult> UpdateShape(int id, [FromBody] GeoJsonLineStringGeometry body, CancellationToken ct = default)
    {
        var route = await _db.CanonicalRoutes.FindAsync([id], ct);
        if (route is null) return NotFound();

        var shape = await _routeManager.GetActiveShapeForRouteAsync(id, ct);
        if (shape is null) return NotFound();

        var coords = body.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
        if (coords.Length < 2)
            return BadRequest(new { error = "LineString must have at least 2 coordinates" });

        var gf = new GeometryFactory(new PrecisionModel(), 4326);
        shape.Geometry = gf.CreateLineString(coords);
        shape.IsManuallyEdited = true;

        route.Geometry = shape.Geometry;
        route.ShapeEdited = true;

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{id}/stops")]
    public async Task<ActionResult<List<StationResponse>>> GetStops(int id, CancellationToken ct = default)
    {
        var stops = await _scheduleManager.GetRouteStopsAsync(id, ct);
        return Ok(stops);
    }

    [HttpGet("{id}/trips")]
    public async Task<ActionResult<List<TripResponse>>> GetTrips(
        int id,
        [FromQuery] string? date = null,
        CancellationToken ct = default)
    {
        var parsedDate = date is not null ? DateOnly.Parse(date) : DateOnly.FromDateTime(DateTime.UtcNow);
        var trips = await _scheduleManager.GetRouteTripsAsync(id, parsedDate, ct);
        return Ok(trips);
    }
}
