using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("feed-versions")]
[Authorize(Policy = PermissionKeys.FeedVersionsView)]
public class FeedVersionsController : ControllerBase
{
    private readonly TransitDbContext _db;

    public FeedVersionsController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<Paginated<FeedVersionResponse>>> GetAll(
        [FromQuery] int? feedId = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.FeedVersions
            .OrderByDescending(fv => fv.FetchedAt)
            .AsQueryable();

        if (feedId.HasValue)
            query = query.Where(fv => fv.FeedId == feedId.Value);

        var total = await query.CountAsync(ct);
        var versions = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(FeedVersionMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(new Paginated<FeedVersionResponse>(versions, total, page, perPage));
    }

    [HttpGet("{sha1}")]
    public async Task<ActionResult<FeedVersionResponse>> GetBySha1(string sha1, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Where(fv => fv.Sha1 == sha1)
            .Select(FeedVersionMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return NotFound();

        return Ok(version);
    }

    [HttpGet("{sha1}/stops")]
    public async Task<ActionResult<Paginated<RawStopResponse>>> GetStops(
        string sha1,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Where(fv => fv.Sha1 == sha1)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return NotFound();

        var query = _db.RawStops
            .Where(rs => rs.FeedVersionId == version.Id);

        var total = await query.CountAsync(ct);
        var stops = await query
            .OrderBy(rs => rs.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(RawStopMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(new Paginated<RawStopResponse>(stops, total, page, perPage));
    }
}


