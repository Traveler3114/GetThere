using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(Roles = "Admin")]
public class FeedsController : ControllerBase
{
    private readonly FeedManager _feedService;

public FeedsController(FeedManager feedManager) { _feedService = feedManager; }

    [HttpGet]
    public async Task<ActionResult<Paginated<FeedResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] bool showInternal = false,
        CancellationToken ct = default)
    {
        var (feeds, total) = await _feedService.GetAllAsync(page, perPage, showInternal, ct);
        return Ok(new Paginated<FeedResponse>(feeds, total, page, perPage));
    }

    [HttpPost]
    public async Task<ActionResult<FeedResponse>> Create(
        [FromBody] CreateFeedRequest request,
        CancellationToken ct = default)
    {
        var feed = await _feedService.CreateAsync(request.OperatorId, request.FeedType, request.FeedId, request.Url, request.RefreshIntervalSeconds, ct);
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
        return Ok(logs);
    }

    [HttpPost("{id}/fetch")]
    public async Task<ActionResult> Fetch(int id, CancellationToken ct = default)
    {
        var version = await _feedService.TriggerImportAsync(id, ct);
        return Ok(new { message = $"Import succeeded: {version.RouteCount} routes, {version.StopCount} stops, {version.TripCount} trips." });
    }

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<FeedVersionResponse>>> GetVersions(int id, CancellationToken ct = default)
    {
        var versions = await _feedService.GetFeedVersionsAsync(id, ct);
        var dtos = versions.Select(FeedVersionMapper.ToResponse).ToList();
        return Ok(dtos);
    }
}
