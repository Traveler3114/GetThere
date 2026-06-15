using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class AgenciesController : ControllerBase
{
    private readonly TransitDbContext _db;

    public AgenciesController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<AgencyDto>>>> GetAll(
        [FromQuery] int? feedId = null,
        [FromQuery] int? operatorId = null,
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
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

        if (after > 0)
            query = query.Where(a => a.Id > after);

        var agencies = await query
            .Take(perPage)
            .Select(a => new AgencyDto
            {
                Id = a.Id,
                AgencyId = a.AgencyId,
                Name = a.Name,
                Url = a.Url,
                Timezone = a.Timezone,
                Phone = a.Phone,
                OperatorId = a.OperatorId,
                OperatorName = a.Operator != null ? a.Operator.Name : null,
                FeedVersionId = a.FeedVersionId
            })
            .ToListAsync(ct);

        var nextAfter = agencies.Count > 0 ? agencies.Last().Id : after;
        var total = await _db.Agencies.CountAsync(ct);
        var nextUrl = agencies.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<AgencyDto>>.OkPaginated(agencies, nextAfter, total, nextUrl));
    }
}

public class AgencyDto
{
    public int Id { get; set; }
    public string AgencyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Timezone { get; set; }
    public string? Phone { get; set; }
    public int? OperatorId { get; set; }
    public string? OperatorName { get; set; }
    public int FeedVersionId { get; set; }
}
