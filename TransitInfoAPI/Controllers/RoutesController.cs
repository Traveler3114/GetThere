using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Geometries;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class RoutesController : ControllerBase
{
    private readonly RouteManager _routeService;
    private readonly ScheduleManager _scheduleService;
    private readonly TransitDbContext _db;

    public RoutesController(RouteManager RouteManager, ScheduleManager ScheduleManager, TransitDbContext db)
    {
        _routeService = RouteManager;
        _scheduleService = ScheduleManager;
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
        [FromQuery] int perPage = 50,
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

            var routes = await query.OrderBy(r => r.Id).ToListAsync(ct);

            var fc = GeoJsonGeometry.ToLineStringCollection(routes,
                r => r.Geometry,
                r => new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["globalId"] = r.GlobalId,
                    ["onestopId"] = r.OnestopId,
                    ["name"] = r.LongName,
                    ["shortName"] = r.ShortName,
                    ["routeType"] = r.RouteType.ToString(),
                    ["operatorId"] = r.OperatorId
                });
            return Ok(fc);
        }

        var result = await _routeService.GetAllAsync(operatorId, routeType, q, page, perPage, ct);
        var total = await _db.CanonicalRoutes.CountAsync(r => r.IsActive, ct);
        return Ok(new Paginated<RouteDto>(result, total, page, perPage));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RouteDto>> GetById(int id, CancellationToken ct = default)
    {
        var route = await _db.CanonicalRoutes
            .Where(r => r.Id == id)
            .Select(r => new RouteDto
            {
                Id = r.Id,
                GlobalId = r.GlobalId,
                OnestopId = r.OnestopId,
                Name = r.LongName,
                ShortName = r.ShortName,
                RouteType = r.RouteType.ToString(),
                OperatorId = r.OperatorId,
                OperatorName = r.Operator != null ? r.Operator.Name : null
            })
            .FirstOrDefaultAsync(ct);

        if (route is null)
            return NotFound();

        return Ok(route);
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<RouteDto>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var route = await _db.CanonicalRoutes
            .Where(r => r.OnestopId == onestopId)
            .Select(r => new RouteDto
            {
                Id = r.Id,
                GlobalId = r.GlobalId,
                OnestopId = r.OnestopId,
                Name = r.LongName,
                ShortName = r.ShortName,
                RouteType = r.RouteType.ToString(),
                OperatorId = r.OperatorId,
                OperatorName = r.Operator != null ? r.Operator.Name : null
            })
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

    [HttpGet("{id}/stops")]
    public async Task<ActionResult<List<StationDto>>> GetStops(int id, CancellationToken ct = default)
    {
        var stops = await _scheduleService.GetRouteStopsAsync(id, ct);
        return Ok(stops);
    }

    [HttpGet("{id}/trips")]
    public async Task<ActionResult<List<TripDto>>> GetTrips(
        int id,
        [FromQuery] string? date = null,
        CancellationToken ct = default)
    {
        var parsedDate = date is not null ? DateOnly.Parse(date) : DateOnly.FromDateTime(DateTime.UtcNow);
        var trips = await _scheduleService.GetRouteTripsAsync(id, parsedDate, ct);
        return Ok(trips);
    }
}
