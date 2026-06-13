using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class OperatorsController : ControllerBase
{
    private readonly TransitDbContext _db;

    public OperatorsController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<List<Operator>>> GetAll(
        [FromQuery] int? countryId = null,
        [FromQuery] OperatorType? type = null,
        CancellationToken ct = default)
    {
        var query = _db.Operators.Include(o => o.Country).AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);
        if (type.HasValue)
            query = query.Where(o => o.OperatorType == type.Value);

        return await query.ToListAsync(ct);
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<Operator>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var op = await _db.Operators
            .Include(o => o.Country)
            .FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);

        if (op is null) return NotFound();
        return op;
    }

    [HttpGet("{globalId}/stations")]
    public async Task<ActionResult<List<CanonicalStation>>> GetStations(string globalId, CancellationToken ct = default)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return NotFound();

        var stations = await _db.CanonicalStationOperators
            .Include(cso => cso.CanonicalStation)
            .Where(cso => cso.OperatorId == op.Id)
            .Select(cso => cso.CanonicalStation)
            .ToListAsync(ct);

        return stations;
    }

    [HttpGet("{globalId}/routes")]
    public async Task<ActionResult<List<CanonicalRoute>>> GetRoutes(string globalId, CancellationToken ct = default)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return NotFound();

        return await _db.CanonicalRoutes
            .Where(r => r.OperatorId == op.Id && r.IsActive)
            .ToListAsync(ct);
    }

    [HttpGet("{globalId}/feeds")]
    public async Task<ActionResult<List<Feed>>> GetFeeds(string globalId, CancellationToken ct = default)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return NotFound();

        return await _db.Feeds
            .Where(f => f.OperatorId == op.Id && f.IsActive)
            .ToListAsync(ct);
    }
}
