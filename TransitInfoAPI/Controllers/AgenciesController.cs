using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class AgenciesController : ControllerBase
{
    private readonly TransitDbContext _db;

    public AgenciesController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<Paginated<AgencyDto>>> GetAll(
        [FromQuery] int? feedId = null,
        [FromQuery] int? operatorId = null,
        [FromQuery] int page = 1,
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

        var total = await query.CountAsync(ct);
        var agencies = await query
            .Skip((page - 1) * perPage)
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

        return Ok(new Paginated<AgencyDto>(agencies, total, page, perPage));
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
