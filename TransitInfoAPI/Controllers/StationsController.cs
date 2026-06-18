using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class StationsController : ControllerBase
{
    private readonly StationManager _stationService;
    private readonly TransitDbContext _db;

    public StationsController(StationManager StationManager, TransitDbContext db)
    {
        _stationService = StationManager;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int? countryId,
        [FromQuery] string? format = null,
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var result = await _stationService.GetAllAsync(lat, lon, radiusKm, countryId, after, perPage, ct);

        if (format == "geojson")
        {
            var fc = GeoJsonGeometry.ToPointCollection(result,
                s => s.Latitude, s => s.Longitude,
                s => new Dictionary<string, object?>
                {
                    ["id"] = s.Id,
                    ["globalId"] = s.GlobalId,
                    ["onestopId"] = s.OnestopId,
                    ["name"] = s.Name,
                    ["stationType"] = s.StationType,
                    ["routeType"] = s.PrimaryRouteType,
                    ["primaryRouteType"] = s.PrimaryRouteType,
                    ["countryName"] = s.CountryName,
                    ["cityName"] = s.CityName
                });
            return Ok(fc);
        }

        var nextAfter = result.Count > 0 ? result.Max(r => r.Id) : after;
        var total = await _stationService.GetTotalCountAsync(ct);
        var nextUrl = result.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<StationDto>>.OkPaginated(result, nextAfter, total, nextUrl));
    }

    [HttpGet("search")]
    public async Task<ActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] RouteType? routeType,
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var result = await _stationService.SearchAsync(q, routeType, after, perPage, ct);
        var nextAfter = result.Count > 0 ? result.Max(r => r.Id) : after;
        var total = await _stationService.GetTotalCountAsync(ct);
        var nextUrl = result.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<StationDto>>.OkPaginated(result, nextAfter, total, nextUrl));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OperationResult<StationDto>>> GetById(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound(OperationResult<StationDto>.Fail("Station not found."));
        return Ok(OperationResult<StationDto>.Ok(station));
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<OperationResult<StationDto>>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByOnestopIdAsync(onestopId, ct);
        if (station is null) return NotFound(OperationResult<StationDto>.Fail("Station not found."));
        return Ok(OperationResult<StationDto>.Ok(station));
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<OperationResult<List<StationOperatorDto>>>> GetOperators(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        var operators = await _stationService.GetOperatorsAsync(station.OnestopId, ct);
        return Ok(OperationResult<List<StationOperatorDto>>.Ok(operators));
    }

    [HttpGet("{id}/routes")]
    public async Task<ActionResult<OperationResult<List<RouteDto>>>> GetRoutes(int id, CancellationToken ct = default)
    {
        var routeIds = await _db.StopTimes
            .Where(st => st.CanonicalStationId == id)
            .Where(st => st.Trip.CanonicalRouteId != null)
            .Select(st => st.Trip.CanonicalRouteId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var routes = await _db.CanonicalRoutes
            .Where(r => routeIds.Contains(r.Id))
            .Select(r => new RouteDto
            {
                Id = r.Id,
                GlobalId = r.GlobalId,
                OnestopId = r.OnestopId,
                Name = r.LongName,
                ShortName = r.ShortName,
                RouteType = r.RouteType.ToString(),
                OperatorId = r.OperatorId,
                OperatorName = r.Operator.Name
            })
            .ToListAsync(ct);

        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    [HttpGet("{id}/departures")]
    public async Task<ActionResult<OperationResult<List<DepartureDto>>>> GetDepartures(
        int id,
        [FromQuery] DateTime? from = null,
        [FromQuery] int count = 10,
        CancellationToken ct = default)
    {
        var departures = await _stationService.GetDeparturesAsync(id, from, count, ct);
        return Ok(OperationResult<List<DepartureDto>>.Ok(departures));
    }
}
