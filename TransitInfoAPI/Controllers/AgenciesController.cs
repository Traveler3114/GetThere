using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(Policy = PermissionKeys.AgenciesView)]
public class AgenciesController : ControllerBase
{
    private readonly TransitDbContext _db;

    public AgenciesController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<Paginated<AgencyResponse>>> GetAll(
        [FromQuery] int? feedId = null,
        [FromQuery] int? operatorId = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.Agencies
            .Include(a => a.Operator)
            .Include(a => a.FeedVersion)
            .OrderBy(a => a.Id)
            .AsQueryable();

        if (feedId.HasValue)
            query = query.Where(a => a.FeedVersion.FeedId == feedId.Value);

        if (operatorId.HasValue)
            query = query.Where(a => a.OperatorId == operatorId.Value);

        var total = await query.CountAsync(ct);
        var agencies = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(AgencyMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(new Paginated<AgencyResponse>(agencies, total, page, perPage));
    }
}