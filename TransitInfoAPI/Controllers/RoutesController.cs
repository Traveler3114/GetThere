using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class RoutesController : ControllerBase
{
    private readonly TransitDbContext _db;

    public RoutesController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<List<CanonicalRoute>>> GetAll(
        [FromQuery] int? operatorId,
        [FromQuery] RouteType? routeType,
        CancellationToken ct = default)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);

        return await query.ToListAsync(ct);
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<CanonicalRoute>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var route = await _db.CanonicalRoutes
            .FirstOrDefaultAsync(r => r.GlobalId == globalId && r.IsActive, ct);

        if (route is null) return NotFound();
        return route;
    }
}
