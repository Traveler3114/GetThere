using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
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

    public OperatorsController(OperatorManager OperatorManager, TransitDbContext db, OnestopIdManager OnestopIdManager)
    {
        _operatorService = OperatorManager;
        _db = db;
        _onestopIdService = OnestopIdManager;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] int? countryId = null,
        [FromQuery] OperatorType? type = null,
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
            if (type.HasValue)
                query = query.Where(o => o.OperatorType == type.Value);

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
                        ["shortName"] = item.Operator.ShortName,
                        ["operatorType"] = item.Operator.OperatorType.ToString(),
                        ["isVerified"] = item.Operator.IsVerified,
                        ["isVirtual"] = item.Operator.IsVirtual
                    }
                }).ToList()
            };
            return Ok(fc);
        }

        var result = await _operatorService.GetAllAsync(countryId, type, q, page, perPage, ct);
        var total = await _db.Operators.CountAsync(ct);
        return Ok(new Paginated<OperatorDto>(result, total, page, perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OperatorDto>> GetById(int id, CancellationToken ct = default)
    {
        var op = await _db.Operators
            .Include(o => o.Country)
            .Where(o => o.Id == id)
            .Select(o => new OperatorDto
            {
                Id = o.Id,
                GlobalId = o.GlobalId,
                OnestopId = o.OnestopId,
                Name = o.Name,
                ShortName = o.ShortName,
                Website = o.Website,
                OperatorType = o.OperatorType.ToString(),
                IsVerified = o.IsVerified,
                IsVirtual = o.IsVirtual,
                CountryName = o.Country != null ? o.Country.Name : null
            })
            .FirstOrDefaultAsync(ct);

        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<OperatorDto>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var op = await _db.Operators
            .Include(o => o.Country)
            .Where(o => o.OnestopId == onestopId)
            .Select(o => new OperatorDto
            {
                Id = o.Id,
                GlobalId = o.GlobalId,
                OnestopId = o.OnestopId,
                Name = o.Name,
                ShortName = o.ShortName,
                Website = o.Website,
                OperatorType = o.OperatorType.ToString(),
                IsVerified = o.IsVerified,
                IsVirtual = o.IsVirtual,
                CountryName = o.Country != null ? o.Country.Name : null
            })
            .FirstOrDefaultAsync(ct);

        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<OperatorDto>> GetByGlobalId(string globalId, CancellationToken ct = default)
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
            { 1, ("bus.png", "#1f78b4") },
            { 2, ("trolleybus.png", "#33a02c") },
            { 3, ("metro.png", "#e31a1c") },
            { 4, ("train.png", "#b15928") },
            { 5, ("ferry.png", "#6a3d9a") },
            { 6, ("flight.png", "#b2df8a") },
            { 7, ("cablecar.png", "#fb9a99") },
            { 8, ("funicular.png", "#fdbf6f") },
            { 9, ("coach.png", "#cab2d6") },
            { 10, ("bikeshare.png", "#a6cee3") },
            { 11, ("scootershare.png", "#ff7f00") }
        };

        var types = Enum.GetValues<RouteType>()
            .Select(rt =>
            {
                var id = (int)rt;
                var name = rt switch
                {
                    RouteType.Bus => "Bus",
                    RouteType.Tram => "Tram",
                    RouteType.Trolleybus => "Trolleybus",
                    RouteType.Metro => "Metro",
                    RouteType.Rail => "Train",
                    RouteType.Ferry => "Ferry",
                    RouteType.Flight => "Flight",
                    RouteType.CableCar => "Cable Car",
                    RouteType.Funicular => "Funicular",
                    RouteType.Coach => "Coach",
                    RouteType.BikeShare => "Bike Share",
                    RouteType.ScooterShare => "Scooter Share",
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
    public async Task<ActionResult<List<StationDto>>> GetStations(string globalId, CancellationToken ct = default)
    {
        var stations = await _operatorService.GetStationsAsync(globalId, ct);
        return Ok(new Paginated<StationDto>(stations, stations.Count, 1, stations.Count));
    }

    [HttpGet("{globalId}/routes")]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(string globalId, CancellationToken ct = default)
    {
        var routes = await _operatorService.GetRoutesAsync(globalId, ct);
        return Ok(new Paginated<RouteDto>(routes, routes.Count, 1, routes.Count));
    }

    [HttpGet("{globalId}/feeds")]
    public async Task<ActionResult<List<FeedDto>>> GetFeeds(string globalId, CancellationToken ct = default)
    {
        var feeds = await _operatorService.GetFeedsAsync(globalId, ct);
        return Ok(new Paginated<FeedDto>(feeds, feeds.Count, 1, feeds.Count));
    }

    [HttpPost]
    public async Task<ActionResult<OperatorDto>> Create([FromBody] CreateOperatorRequest request, CancellationToken ct = default)
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

        if (!Enum.TryParse<OperatorType>(request.OperatorType, true, out var operatorType))
            return Problem(statusCode: 400, title: $"Invalid operator type '{request.OperatorType}'.");

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
            OperatorType = operatorType,
            IsVerified = false,
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

        var dto = new OperatorDto
        {
            Id = op.Id,
            GlobalId = op.GlobalId,
            OnestopId = op.OnestopId,
            Name = op.Name,
            ShortName = op.ShortName,
            Website = op.Website,
            OperatorType = op.OperatorType.ToString(),
            IsVerified = op.IsVerified,
            CountryName = country.Name
        };

        return CreatedAtAction(nameof(GetAll), null, dto);
    }

    [HttpPut("{globalId}")]
    public async Task<ActionResult<OperatorDto>> Update(string globalId, [FromBody] UpdateOperatorRequest request, CancellationToken ct = default)
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
            op.Name = request.Name;

        if (request.ShortName is not null)
            op.ShortName = request.ShortName;

        if (request.Website is not null)
            op.Website = request.Website;

        if (request.OperatorType is not null)
        {
            if (!Enum.TryParse<OperatorType>(request.OperatorType, true, out var operatorType))
                return Problem(statusCode: 400, title: $"Invalid operator type '{request.OperatorType}'.");
            op.OperatorType = operatorType;
        }

        await _db.SaveChangesAsync(ct);

        var dto = new OperatorDto
        {
            Id = op.Id,
            GlobalId = op.GlobalId,
            OnestopId = op.OnestopId,
            Name = op.Name,
            ShortName = op.ShortName,
            Website = op.Website,
            OperatorType = op.OperatorType.ToString(),
            IsVerified = op.IsVerified,
            CountryName = op.Country.Name
        };

        return Ok(dto);
    }
}
