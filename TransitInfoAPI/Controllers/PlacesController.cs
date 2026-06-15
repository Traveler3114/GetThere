using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class PlacesController : ControllerBase
{
    private readonly TransitDbContext _db;

    public PlacesController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<PlaceDto>>>> GetAll(
        [FromQuery] string? countryCode = null,
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.Places
            .OrderBy(p => p.Id)
            .AsQueryable();

        if (!string.IsNullOrEmpty(countryCode))
            query = query.Where(p => p.AdmCountryCode == countryCode);

        if (after > 0)
            query = query.Where(p => p.Id > after);

        var places = await query
            .Take(perPage)
            .Select(p => new PlaceDto
            {
                Id = p.Id,
                Name = p.Name,
                AdmCountryCode = p.AdmCountryCode,
                AdmRegionCode = p.AdmRegionCode,
                Lat = p.Lat,
                Lon = p.Lon,
                Population = p.Population
            })
            .ToListAsync(ct);

        var nextAfter = places.Count > 0 ? places.Last().Id : after;
        var total = await _db.Places.CountAsync(ct);
        var nextUrl = places.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<PlaceDto>>.OkPaginated(places, nextAfter, total, nextUrl));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OperationResult<PlaceDto>>> GetById(int id, CancellationToken ct = default)
    {
        var place = await _db.Places
            .Where(p => p.Id == id)
            .Select(p => new PlaceDto
            {
                Id = p.Id,
                Name = p.Name,
                AdmCountryCode = p.AdmCountryCode,
                AdmRegionCode = p.AdmRegionCode,
                Lat = p.Lat,
                Lon = p.Lon,
                Population = p.Population
            })
            .FirstOrDefaultAsync(ct);

        if (place is null)
            return NotFound(OperationResult<PlaceDto>.Fail("Place not found."));

        return Ok(OperationResult<PlaceDto>.Ok(place));
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<OperationResult<List<OperatorDto>>>> GetOperators(int id, CancellationToken ct = default)
    {
        var operators = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == id)
            .SelectMany(cs => cs.StationOperators)
            .Select(cso => cso.Operator)
            .Distinct()
            .Include(o => o.Country)
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
            .ToListAsync(ct);

        return Ok(OperationResult<List<OperatorDto>>.Ok(operators));
    }

    [HttpGet("{id}/stations")]
    public async Task<ActionResult<OperationResult<List<StationDto>>>> GetStations(int id, CancellationToken ct = default)
    {
        var stations = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == id)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                OnestopId = cs.OnestopId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                CountryName = null,
                CityName = null
            })
            .ToListAsync(ct);

        return Ok(OperationResult<List<StationDto>>.Ok(stations));
    }
}
