using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class PlacesController : ControllerBase
{
    private readonly TransitDbContext _db;

    public PlacesController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<Paginated<PlaceResponse>>> GetAll(
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
            .Select(PlaceMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(new Paginated<PlaceResponse>(places, total, page, perPage));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PlaceResponse>> GetById(int id, CancellationToken ct = default)
    {
        var place = await _db.Places
            .Where(p => p.Id == id)
            .Select(PlaceMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);

        if (place is null)
            return NotFound();

        return Ok(place);
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<List<OperatorResponse>>> GetOperators(int id, CancellationToken ct = default)
    {
        var operatorIds = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == id)
            .SelectMany(cs => cs.StationOperators)
            .Select(cso => cso.OperatorId)
            .Distinct()
            .ToListAsync(ct);
        var operators = await _db.Operators
            .Where(o => operatorIds.Contains(o.Id))
            .Include(o => o.Country)
            .Select(OperatorMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(new Paginated<OperatorResponse>(operators, operators.Count, 1, operators.Count));
    }

    [HttpGet("{id}/stations")]
    public async Task<ActionResult<List<StationResponse>>> GetStations(int id, CancellationToken ct = default)
    {
        var stations = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == id)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(new Paginated<StationResponse>(stations, stations.Count, 1, stations.Count));
    }
}
