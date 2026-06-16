using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class FeedsController : ControllerBase
{
    private readonly FeedService _feedService;
    private readonly TransitDbContext _db;
    private readonly ILogger<FeedsController> _logger;

    public FeedsController(
        FeedService feedService,
        TransitDbContext db,
        ILogger<FeedsController> logger)
    {
        _feedService = feedService;
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<FeedDto>>>> GetAll(
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var feeds = await _feedService.GetAllAsync(after, perPage, ct);
        var nextAfter = feeds.Count > 0 ? feeds.Last().Id : after;
        var total = await _db.Feeds.CountAsync(ct);
        var nextUrl = feeds.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<FeedDto>>.OkPaginated(feeds, nextAfter, total, nextUrl));
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<FeedDto>>> Create(
        [FromQuery] int operatorId,
        [FromQuery] FeedType feedType,
        [FromQuery] SourceType sourceType,
        [FromQuery] string feedId,
        [FromQuery] string? externalUrl,
        [FromQuery] int refreshIntervalSeconds = 3600,
        CancellationToken ct = default)
    {
        var feed = await _feedService.CreateAsync(operatorId, feedType, sourceType, feedId, externalUrl, refreshIntervalSeconds, ct);
        var dto = await _feedService.GetByIdAsync(feed.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { }, OperationResult<FeedDto>.Ok(dto!));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult>> Update(int id, [FromBody] Feed updated, CancellationToken ct = default)
    {
        var (success, message) = await _feedService.UpdateAsync(id, updated, ct);
        if (!success) return NotFound(OperationResult.Fail(message!));
        return Ok(OperationResult.Ok("Feed updated."));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(int id, CancellationToken ct = default)
    {
        var success = await _feedService.DeleteAsync(id, ct);
        if (!success) return NotFound(OperationResult.Fail("Feed not found."));
        return Ok(OperationResult.Ok("Feed deleted."));
    }

    [HttpPost("{id}/fetch")]
    public async Task<ActionResult<OperationResult>> Fetch(int id, CancellationToken ct = default)
    {
        try
        {
            var version = await _feedService.TriggerImportAsync(id, ct);
            return Ok(OperationResult.Ok(
                $"Import succeeded: {version.RouteCount} routes, {version.StopCount} stops, {version.TripCount} trips."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch/import failed for feed {Id}", id);
            return StatusCode(500, OperationResult.Fail($"Import failed: {ex.Message}"));
        }
    }

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<OperationResult<List<FeedVersionDto>>>> GetVersions(int id, CancellationToken ct = default)
    {
        var versions = await _feedService.GetFeedVersionsAsync(id, ct);
        var dtos = versions.Select(v => new FeedVersionDto
        {
            Id = v.Id,
            FeedId = v.FeedId,
            Sha1 = v.Sha1,
            FetchedAt = v.FetchedAt,
            ImportedAt = v.ImportedAt,
            IsActive = v.IsActive,
            ImportStatus = v.ImportStatus.ToString(),
            ImportError = v.ImportError,
            ServiceLevelStart = v.ServiceLevelStart,
            ServiceLevelEnd = v.ServiceLevelEnd,
            StopCount = v.StopCount,
            RouteCount = v.RouteCount,
            TripCount = v.TripCount
        }).ToList();
        return Ok(OperationResult<List<FeedVersionDto>>.Ok(dtos));
    }
}
