using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class StationsController : ControllerBase
{
    private readonly TransitDbContext _db;

    public StationsController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<List<CanonicalStation>>> GetAll(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int? countryId,
        CancellationToken ct = default)
    {
        var query = _db.CanonicalStations
            .Include(cs => cs.Country)
            .Where(cs => cs.IsActive)
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

        return await query.ToListAsync(ct);
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<CanonicalStation>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var station = await _db.CanonicalStations
            .Include(cs => cs.Country)
            .Include(cs => cs.City)
            .FirstOrDefaultAsync(cs => cs.GlobalId == globalId && cs.IsActive, ct);

        if (station is null) return NotFound();
        return station;
    }

    [HttpGet("{globalId}/operators")]
    public async Task<ActionResult<List<object>>> GetOperators(string globalId, CancellationToken ct = default)
    {
        var station = await _db.CanonicalStations
            .FirstOrDefaultAsync(cs => cs.GlobalId == globalId && cs.IsActive, ct);

        if (station is null) return NotFound();

        var operators = await _db.CanonicalStationOperators
            .Include(cso => cso.Operator)
            .Where(cso => cso.CanonicalStationId == station.Id)
            .Select(cso => new
            {
                cso.Operator.GlobalId,
                cso.Operator.Name,
                cso.Operator.OperatorType,
                cso.PlatformInfo
            })
            .ToListAsync(ct);

        return operators.Cast<object>().ToList();
    }

    [HttpGet("{globalId}/departures")]
    public async Task<ActionResult<List<object>>> GetDepartures(string globalId, CancellationToken ct = default)
    {
        // TODO: Query OTP for departures at this station
        return new List<object>();
    }
}
