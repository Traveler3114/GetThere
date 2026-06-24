using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Managers;

using Microsoft.Data.SqlClient;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class OperatorsController : ControllerBase
{
    private readonly OperatorManager _operatorService;
    private readonly TransitDbContext _db;
    private readonly OnestopIdManager _onestopIdService;

    public OperatorsController(OperatorManager operatorManager, TransitDbContext db, OnestopIdManager onestopIdManager)
    {
        _operatorService = operatorManager;
        _db = db;
        _onestopIdService = onestopIdManager;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] int? countryId = null,
        [FromQuery] string? q = null,
        [FromQuery] string? format = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        if (format == "geojson")
        {
            var query = _db.Operators.AsQueryable();

            if (countryId.HasValue)
                query = query.Where(o => o.CountryId == countryId.Value);

            var operators = await query.OrderBy(o => o.Id).Skip((page - 1) * perPage).Take(perPage)
                .Select(o => new
                {
                    Operator = o,
                    StationLat = o.StationOperators
                        .Select(cso => (double?)cso.CanonicalStation.Latitude)
                        .FirstOrDefault(),
                    StationLon = o.StationOperators
                        .Select(cso => (double?)cso.CanonicalStation.Longitude)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var fc = new GeoJsonFeatureCollection
            {
                Features = operators.Select(item => new GeoJsonFeature
                {
                    Geometry = item.StationLat.HasValue && item.StationLon.HasValue
                        ? new { type = "Point", coordinates = new[] { item.StationLon.Value, item.StationLat.Value } }
                        : null,
                    Properties = new Dictionary<string, object?>
                    {
                        ["id"] = item.Operator.Id,
                        ["globalId"] = item.Operator.GlobalId,
                        ["onestopId"] = item.Operator.OnestopId,
                        ["name"] = item.Operator.Name,
                        ["shortName"] = item.Operator.ShortName
                    }
                }).ToList()
            };
            return Ok(fc);
        }

        var result = await _operatorService.GetAllAsync(countryId, q, page, perPage, ct);
        var total = await _db.Operators.CountAsync(ct);
        if (countryId.HasValue)
            total = await _db.Operators.CountAsync(o => o.CountryId == countryId.Value, ct);
        if (!string.IsNullOrWhiteSpace(q))
            total = await _db.Operators.CountAsync(o =>
                (!countryId.HasValue || o.CountryId == countryId.Value) &&
                (o.Name.Contains(q) || o.ShortName.Contains(q)), ct);
        return Ok(new Paginated<OperatorResponse>(result, total, page, perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OperatorResponse>> GetById(int id, CancellationToken ct = default)
    {
        var op = await _db.Operators
            .Include(o => o.Country)
            .Where(o => o.Id == id)
            .Select(OperatorMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);

        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<OperatorResponse>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var op = await _db.Operators
            .Include(o => o.Country)
            .Where(o => o.OnestopId == onestopId)
            .Select(OperatorMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);

        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<OperatorResponse>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var op = await _operatorService.GetByGlobalIdAsync(globalId, ct);
        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("types")]
    public ActionResult<List<object>> GetTypes()
    {
        var icons = new Dictionary<int, (string Icon, string Color)>
        {
            { 0, ("tram.png", "#126400") },
            { 1, ("subway.png", "#e31a1c") },
            { 2, ("train.png", "#b15928") },
            { 3, ("bus.png", "#1f78b4") },
            { 4, ("ferry.png", "#6a3d9a") },
            { 5, ("cabletram.png", "#fb9a99") },
            { 6, ("cablecar.png", "#fb9a99") },
            { 7, ("funicular.png", "#fdbf6f") },
            { 11, ("trolleybus.png", "#33a02c") },
            { 12, ("monorail.png", "#cab2d6") },
            { 100, ("bicycle.png", "#a6cee3") },
            { 101, ("scooter.png", "#ff7f00") },
            { 200, ("airplane.png", "#b2df8a") }
        };

        var types = Enum.GetValues<RouteType>()
            .Select(rt =>
            {
                var id = (int)rt;
                var name = rt switch
                {
                    RouteType.Tram => "Tram",
                    RouteType.Subway => "Subway",
                    RouteType.Train => "Train",
                    RouteType.Bus => "Bus",
                    RouteType.Ferry => "Ferry",
                    RouteType.CableTram => "Cable Tram",
                    RouteType.CableCar => "Cable Car",
                    RouteType.Funicular => "Funicular",
                    RouteType.Trolleybus => "Trolleybus",
                    RouteType.Monorail => "Monorail",
                    RouteType.Bicycle => "Bicycle",
                    RouteType.Scooter => "Scooter",
                    RouteType.Airplane => "Airplane",
                    _ => rt.ToString()
                };
                icons.TryGetValue(id, out var meta);
                return new { Id = id, Name = name, IconFile = meta.Icon ?? $"{rt.ToString().ToLower()}.png", Color = meta.Color ?? "#808080" };
            })
            .ToList<object>();
        return Ok(types);
    }

    [HttpGet("{id:int}/service-area")]
    public async Task<ActionResult> GetServiceArea(int id, CancellationToken ct = default)
    {
        var hull = await _db.FeedVersions
            .Where(fv => fv.ConvexHull != null && fv.Agencies.Any(a => a.OperatorId == id))
            .OrderByDescending(fv => fv.ImportedAt)
            .Select(fv => fv.ConvexHull)
            .FirstOrDefaultAsync(ct);

        Geometry geom;
        if (hull is not null)
        {
            geom = hull;
        }
        else
        {
            var coords = await _db.CanonicalStationOperators
                .Where(cso => cso.OperatorId == id)
                .Select(cso => cso.CanonicalStation)
                .Where(cs => cs != null && cs.IsActive)
                .Select(cs => new Coordinate(cs.Longitude, cs.Latitude))
                .ToListAsync(ct);

            if (coords.Count == 0)
                return NotFound(new { type = "Feature", geometry = (object?)null, properties = new { } });

            var computed = new ConvexHull(coords.ToArray(), GeometryFactory.Default).GetConvexHull();
            if (computed is Polygon polygon && !Orientation.IsCCW(polygon.Shell.Coordinates))
                computed = polygon.Reverse();
            geom = computed;
        }

        return Ok(new
        {
            type = "Feature",
            geometry = new
            {
                type = geom.GeometryType,
                coordinates = geom.Coordinates.Select(c => new[] { c.X, c.Y })
            },
            properties = new { }
        });
    }

    [HttpGet("{globalId}/stations")]
    public async Task<ActionResult<List<StationResponse>>> GetStations(string globalId, CancellationToken ct = default)
    {
        var stations = await _operatorService.GetStationsAsync(globalId, ct);
        return Ok(new Paginated<StationResponse>(stations, stations.Count, 1, stations.Count));
    }

    [HttpGet("{globalId}/routes")]
    public async Task<ActionResult<List<RouteResponse>>> GetRoutes(string globalId, CancellationToken ct = default)
    {
        var routes = await _operatorService.GetRoutesAsync(globalId, ct);
        return Ok(new Paginated<RouteResponse>(routes, routes.Count, 1, routes.Count));
    }

    [HttpGet("{globalId}/feeds")]
    public async Task<ActionResult<List<FeedResponse>>> GetFeeds(string globalId, CancellationToken ct = default)
    {
        var feeds = await _operatorService.GetFeedsAsync(globalId, ct);
        return Ok(new Paginated<FeedResponse>(feeds, feeds.Count, 1, feeds.Count));
    }

    [HttpPost]
    public async Task<ActionResult<OperatorResponse>> Create([FromBody] CreateOperatorRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(statusCode: 400, title: "Operator name is required.");

        if (string.IsNullOrWhiteSpace(request.ShortName))
            return Problem(statusCode: 400, title: "Short name is required.");

        var country = await _db.Countries.FindAsync(new object[] { request.CountryId }, ct);
        if (country is null)
            return Problem(statusCode: 400, title: "Country not found.");

        var globalId = request.GlobalId;
        if (string.IsNullOrWhiteSpace(globalId))
            globalId = $"gt-{country.IsoCode.ToLowerInvariant()}-{request.ShortName.ToLowerInvariant()}";

        var exists = await _db.Operators.AnyAsync(o => o.GlobalId == globalId, ct);
        if (exists)
            return Problem(statusCode: 409, title: $"Operator with GlobalId '{globalId}' already exists.");

        var onestopId = _onestopIdService.GenerateOperatorOnestopId(country.IsoCode, request.ShortName);
        var onestopExists = await _db.Operators.AnyAsync(o => o.OnestopId == onestopId, ct);
        if (onestopExists)
            return Problem(statusCode: 409, title: $"Operator with OnestopId '{onestopId}' already exists.");

        var op = new Operator
        {
            GlobalId = globalId,
            OnestopId = onestopId,
            Name = request.Name,
            ShortName = request.ShortName,
            Website = request.Website,
            CountryId = request.CountryId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Operators.Add(op);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Problem(statusCode: 409, title: $"Operator with OnestopId '{onestopId}' already exists.");
        }

        var dto = OperatorMapper.ToResponse(op);

        return CreatedAtAction(nameof(GetAll), null, dto);
    }

    [HttpPut("{globalId}")]
    public async Task<ActionResult<OperatorResponse>> Update(string globalId, [FromBody] UpdateOperatorRequest request, CancellationToken ct = default)
    {
        var op = await _db.Operators.Include(o => o.Country).FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null)
            return NotFound();

        if (request.CountryId.HasValue)
        {
            var country = await _db.Countries.FindAsync(new object[] { request.CountryId.Value }, ct);
            if (country is null)
                return Problem(statusCode: 400, title: "Country not found.");
            op.CountryId = request.CountryId.Value;
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Problem(statusCode: 400, title: "Name cannot be empty.");
            op.Name = request.Name;
        }

        if (request.ShortName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.ShortName))
                return Problem(statusCode: 400, title: "ShortName cannot be empty.");
            op.ShortName = request.ShortName;
        }

        if (request.Website is not null)
            op.Website = request.Website;

        await _db.SaveChangesAsync(ct);

        var dto = OperatorMapper.ToResponse(op);

        return Ok(dto);
    }

    [HttpDelete("{globalId}")]
    public async Task<ActionResult> Delete(string globalId, CancellationToken ct = default)
    {
        var op = await _db.Operators
            .Include(o => o.Agencies)
            .Include(o => o.Feeds)
            .Include(o => o.Routes)
            .Include(o => o.StationOperators)
            .Include(o => o.MobilityProviders)
            .FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);

        if (op is null)
            return NotFound();

        var totalAssociations = op.Agencies.Count + op.Feeds.Count + op.Routes.Count + op.StationOperators.Count + op.MobilityProviders.Count;
        if (totalAssociations > 0)
            return Problem(statusCode: 409, title: $"Cannot delete operator: has {totalAssociations} associated record(s). Remove associations first.");

        _db.Operators.Remove(op);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
