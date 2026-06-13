using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class FeedsController : ControllerBase
{
    private readonly TransitDbContext _db;
    private readonly FeedImportService _feedImport;

    public FeedsController(TransitDbContext db, FeedImportService feedImport)
    {
        _db = db;
        _feedImport = feedImport;
    }

    [HttpGet]
    public async Task<ActionResult<List<Feed>>> GetAll(CancellationToken ct = default)
    {
        return await _db.Feeds
            .Include(f => f.Operator)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);
    }

    [HttpPost]
    public async Task<ActionResult<Feed>> Create(
        [FromQuery] int operatorId,
        [FromQuery] FeedType feedType,
        [FromQuery] SourceType sourceType,
        [FromQuery] string feedId,
        [FromQuery] string? externalUrl,
        [FromQuery] int refreshIntervalSeconds = 3600,
        CancellationToken ct = default)
    {
        var feed = await _feedImport.RegisterFeedAsync(operatorId, feedType, sourceType, feedId, externalUrl, null, refreshIntervalSeconds, ct);
        return CreatedAtAction(nameof(GetAll), new { }, feed);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(int id, [FromBody] Feed updated, CancellationToken ct = default)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { id }, ct);
        if (feed is null) return NotFound();

        feed.FeedType = updated.FeedType;
        feed.ExternalUrl = updated.ExternalUrl;
        feed.InternalUrl = updated.InternalUrl;
        feed.IsActive = updated.IsActive;
        feed.RefreshIntervalSeconds = updated.RefreshIntervalSeconds;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Deactivate(int id, CancellationToken ct = default)
    {
        await _feedImport.DeactivateFeedAsync(id, ct);
        return NoContent();
    }
}
