using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        if (format == "geojson")
        {
            var query = _db.CanonicalStations
                .Include(cs => cs.Country)
                .Where(cs => cs.IsActive && cs.StationType == StationType.Stop)
                .AsQueryable();

            if (countryId.HasValue)
                query = query.Where(cs => cs.CountryId == countryId.Value);

            if (lat is not null && lon is not null && radiusKm is not null)
            {
                var latRange = radiusKm.Value / 111.0;
                var lonRange = radiusKm.Value / (111.0 * Math.Cos(lat.Value * Math.PI / 180));
                query = query.Where(cs =>
                    cs.Latitude >= lat.Value - latRange &&
                    cs.Latitude <= lat.Value + latRange &&
                    cs.Longitude >= lon.Value - lonRange &&
                    cs.Longitude <= lon.Value + lonRange);
            }

            var allStations = await query
                .OrderBy(cs => cs.Id)
                .Select(cs => new StationDto
                {
                    Id = cs.Id,
                    GlobalId = cs.GlobalId,
                    OnestopId = cs.OnestopId,
                    Name = cs.Name,
                    Latitude = cs.Latitude,
                    Longitude = cs.Longitude,
                    StationType = cs.StationType.ToString(),
                    PrimaryRouteType = cs.PrimaryRouteType.ToString(),
                    CountryName = cs.Country.Name,
                    CityName = cs.City != null ? cs.City.Name : null
                })
                .ToListAsync(ct);

            var fc = GeoJsonGeometry.ToPointCollection(allStations,
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

        var result = await _stationService.GetAllAsync(lat, lon, radiusKm, countryId, page, perPage, ct);
        var total = await _stationService.GetTotalCountAsync(lat, lon, radiusKm, countryId, null, ct);
        return Ok(new Paginated<StationDto>(result, total, page, perPage));
    }

    [HttpGet("search")]
    public async Task<ActionResult<Paginated<StationDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] RouteType? routeType,
        [FromQuery] int? countryId,
        [FromQuery] string? countryName = null,
        [FromQuery] string? stationType = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var result = await _stationService.SearchAsync(q, routeType, countryId, countryName, stationType, page, perPage, ct);
        var total = await _stationService.GetTotalCountAsync(null, null, null, countryId, countryName, ct);
        return Ok(new Paginated<StationDto>(result, total, page, perPage));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<StationDto>> GetById(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<StationDto>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByOnestopIdAsync(onestopId, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<List<StationOperatorDto>>> GetOperators(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        var operators = await _stationService.GetOperatorsAsync(station.OnestopId, ct);
        return Ok(new Paginated<StationOperatorDto>(operators, operators.Count, 1, operators.Count));
    }

    [HttpGet("{id}/routes")]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(int id, CancellationToken ct = default)
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

        return Ok(routes);
    }

    [HttpGet("by-global/{globalId}")]
    public async Task<ActionResult<StationDto>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByGlobalIdAsync(globalId, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpPost("{id}/rematch-place")]
    public async Task<IActionResult> RematchPlace(int id, CancellationToken ct = default)
    {
        var station = await _db.CanonicalStations.FindAsync([id], ct);
        if (station is null) return NotFound();
        var placeMatching = HttpContext.RequestServices.GetRequiredService<PlaceMatchingManager>();
        await placeMatching.RematchStationAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id}/departures")]
    public async Task<ActionResult<List<DepartureDto>>> GetDepartures(
        int id,
        [FromQuery] DateTime? from = null,
        [FromQuery] int count = 10,
        CancellationToken ct = default)
    {
        var departures = await _stationService.GetDeparturesAsync(id, from, count, ct);
        return Ok(new Paginated<DepartureDto>(departures, departures.Count, 1, departures.Count));
    }
}
