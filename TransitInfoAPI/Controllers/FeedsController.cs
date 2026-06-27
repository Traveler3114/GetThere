using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;
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
        FeedManager feedManager,
        TransitDbContext db,
        ILogger<FeedsController> logger)
    {
        _feedService = feedManager;
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<Paginated<FeedResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] bool showInternal = false,
        CancellationToken ct = default)
    {
        var feeds = await _feedService.GetAllAsync(page, perPage, showInternal, ct);
        var total = showInternal
            ? await _db.Feeds.CountAsync(ct)
            : await _db.Feeds.CountAsync(f => !f.IsInternal, ct);
        return Ok(new Paginated<FeedResponse>(feeds, total, page, perPage));
    }

    [HttpPost]
    public async Task<ActionResult<FeedResponse>> Create(
        [FromBody] CreateFeedRequest request,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<FeedType>(request.FeedType, true, out var feedType))
            return Problem(statusCode: 400, title: $"Invalid feed type '{request.FeedType}'.");
        var feed = await _feedService.CreateAsync(request.OperatorId, feedType, request.FeedId, request.ExternalUrl, request.RefreshIntervalSeconds, ct);
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
    public async Task<ActionResult<List<FeedVersionResponse>>> GetVersions(int id, CancellationToken ct = default)
    {
        var versions = await _feedService.GetFeedVersionsAsync(id, ct);
        var dtos = versions.Select(FeedVersionMapper.ToResponse).ToList();
        return Ok(new Paginated<FeedVersionResponse>(dtos, dtos.Count, 1, dtos.Count));
    }
}
