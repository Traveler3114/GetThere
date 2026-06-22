using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<ActionResult<Paginated<PlaceDto>>> GetAll(
        [FromQuery] string? countryCode = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.Places
            .OrderBy(p => p.Id)
            .AsQueryable();

        if (!string.IsNullOrEmpty(countryCode))
            query = query.Where(p => p.AdmCountryCode == countryCode);

        var total = await query.CountAsync(ct);
        var places = await query
            .Skip((page - 1) * perPage)
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

        return Ok(new Paginated<PlaceDto>(places, total, page, perPage));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PlaceDto>> GetById(int id, CancellationToken ct = default)
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
            return NotFound();

        return Ok(place);
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<List<OperatorDto>>> GetOperators(int id, CancellationToken ct = default)
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
                CountryName = o.Country != null ? o.Country.Name : null
            })
            .ToListAsync(ct);

        return Ok(new Paginated<OperatorDto>(operators, operators.Count, 1, operators.Count));
    }

    [HttpGet("{id}/stations")]
    public async Task<ActionResult<List<StationDto>>> GetStations(int id, CancellationToken ct = default)
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

        return Ok(new Paginated<StationDto>(stations, stations.Count, 1, stations.Count));
    }
}
