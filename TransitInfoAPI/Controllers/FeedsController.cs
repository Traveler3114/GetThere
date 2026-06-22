using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class FeedsController : ControllerBase
{
    private readonly FeedManager _feedService;
    private readonly TransitDbContext _db;
    private readonly ILogger<FeedsController> _logger;

    public FeedsController(
        FeedManager FeedManager,
        TransitDbContext db,
        ILogger<FeedsController> logger)
    {
        _feedService = FeedManager;
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<Paginated<FeedDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var feeds = await _feedService.GetAllAsync(page, perPage, ct);
        var total = await _db.Feeds.CountAsync(ct);
        return Ok(new Paginated<FeedDto>(feeds, total, page, perPage));
    }

    [HttpPost]
    public async Task<ActionResult<FeedDto>> Create(
        [FromBody] CreateFeedRequest request,
        CancellationToken ct = default)
    {
        var feed = await _feedService.CreateAsync(request.OperatorId, request.FeedType, request.SourceType, request.FeedId, request.ExternalUrl, request.RefreshIntervalSeconds, ct);
        var dto = await _feedService.GetByIdAsync(feed.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { }, dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFeedRequest request, CancellationToken ct = default)
    {
        var (success, message) = await _feedService.UpdateAsync(id, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var success = await _feedService.DeleteAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpGet("versions/{versionId}/logs")]
    public ActionResult<List<string>> GetVersionLogs(int versionId, CancellationToken ct = default)
    {
        var logs = _feedService.GetImportLogs(versionId);
        return Ok(new { data = logs, total = logs.Count });
    }

    [HttpPost("{id}/fetch")]
    public async Task<ActionResult> Fetch(int id, CancellationToken ct = default)
    {
        try
        {
            var version = await _feedService.TriggerImportAsync(id, ct);
            return Ok(new { message = $"Import succeeded: {version.RouteCount} routes, {version.StopCount} stops, {version.TripCount} trips." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch/import failed for feed {Id}", id);
            return Problem(statusCode: 500, title: "Import failed", detail: ex.Message);
        }
    }

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<FeedVersionDto>>> GetVersions(int id, CancellationToken ct = default)
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
        return Ok(new Paginated<FeedVersionDto>(dtos, dtos.Count, 1, dtos.Count));
    }
}
