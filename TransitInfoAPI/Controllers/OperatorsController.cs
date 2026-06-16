using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class OperatorsController : ControllerBase
{
    private readonly OperatorService _operatorService;
    private readonly TransitDbContext _db;
    private readonly OnestopIdService _onestopIdService;

    public OperatorsController(OperatorService operatorService, TransitDbContext db, OnestopIdService onestopIdService)
    {
        _operatorService = operatorService;
        _db = db;
        _onestopIdService = onestopIdService;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] int? countryId = null,
        [FromQuery] OperatorType? type = null,
        [FromQuery] string? format = null,
        [FromQuery] int after = 0,
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
            if (after > 0)
                query = query.Where(o => o.Id > after);

            var operators = await query.OrderBy(o => o.Id).Take(perPage)
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

        var result = await _operatorService.GetAllAsync(countryId, type, after, perPage, ct);
        var nextAfter = result.Count > 0 ? result.Last().Id : after;
        var total = await _db.Operators.CountAsync(ct);
        var nextUrl = result.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<OperatorDto>>.OkPaginated(result, nextAfter, total, nextUrl));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OperationResult<OperatorDto>>> GetById(int id, CancellationToken ct = default)
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

        if (op is null) return NotFound(OperationResult<OperatorDto>.Fail("Operator not found."));
        return Ok(OperationResult<OperatorDto>.Ok(op));
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<OperationResult<OperatorDto>>> GetByOnestopId(string onestopId, CancellationToken ct = default)
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

        if (op is null) return NotFound(OperationResult<OperatorDto>.Fail("Operator not found."));
        return Ok(OperationResult<OperatorDto>.Ok(op));
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<OperationResult<OperatorDto>>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var op = await _operatorService.GetByGlobalIdAsync(globalId, ct);
        if (op is null) return NotFound(OperationResult<OperatorDto>.Fail("Operator not found."));
        return Ok(OperationResult<OperatorDto>.Ok(op));
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
                .Where(cs => cs != null)
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
    public async Task<ActionResult<OperationResult<List<StationDto>>>> GetStations(string globalId, CancellationToken ct = default)
    {
        var stations = await _operatorService.GetStationsAsync(globalId, ct);
        return Ok(OperationResult<List<StationDto>>.Ok(stations));
    }

    [HttpGet("{globalId}/routes")]
    public async Task<ActionResult<OperationResult<List<RouteDto>>>> GetRoutes(string globalId, CancellationToken ct = default)
    {
        var routes = await _operatorService.GetRoutesAsync(globalId, ct);
        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    [HttpGet("{globalId}/feeds")]
    public async Task<ActionResult<OperationResult<List<FeedDto>>>> GetFeeds(string globalId, CancellationToken ct = default)
    {
        var feeds = await _operatorService.GetFeedsAsync(globalId, ct);
        return Ok(OperationResult<List<FeedDto>>.Ok(feeds));
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<OperatorDto>>> Create([FromBody] CreateOperatorRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(OperationResult<OperatorDto>.Fail("Operator name is required."));

        if (string.IsNullOrWhiteSpace(request.ShortName))
            return BadRequest(OperationResult<OperatorDto>.Fail("Short name is required."));

        var country = await _db.Countries.FindAsync(new object[] { request.CountryId }, ct);
        if (country is null)
            return BadRequest(OperationResult<OperatorDto>.Fail("Country not found."));

        var globalId = request.GlobalId;
        if (string.IsNullOrWhiteSpace(globalId))
            globalId = $"gt-{country.IsoCode.ToLowerInvariant()}-{request.ShortName.ToLowerInvariant()}";

        var exists = await _db.Operators.AnyAsync(o => o.GlobalId == globalId, ct);
        if (exists)
            return Conflict(OperationResult<OperatorDto>.Fail($"Operator with GlobalId '{globalId}' already exists."));

        if (!Enum.TryParse<OperatorType>(request.OperatorType, true, out var operatorType))
            return BadRequest(OperationResult<OperatorDto>.Fail($"Invalid operator type '{request.OperatorType}'."));

        var op = new Operator
        {
            GlobalId = globalId,
            OnestopId = _onestopIdService.GenerateOperatorOnestopId(country.IsoCode, request.ShortName),
            Name = request.Name,
            ShortName = request.ShortName,
            Website = request.Website,
            OperatorType = operatorType,
            IsVerified = false,
            CountryId = request.CountryId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Operators.Add(op);
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
            CountryName = country.Name
        };

        return CreatedAtAction(nameof(GetAll), null, OperationResult<OperatorDto>.Ok(dto, "Operator created."));
    }
}
