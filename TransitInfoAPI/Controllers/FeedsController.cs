using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
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

    public FeedsController(FeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<FeedDto>>>> GetAll(CancellationToken ct = default)
    {
        var feeds = await _feedService.GetAllAsync(ct);
        return Ok(OperationResult<List<FeedDto>>.Ok(feeds));
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
    public async Task<ActionResult<OperationResult>> Deactivate(int id, CancellationToken ct = default)
    {
        var success = await _feedService.DeactivateAsync(id, ct);
        if (!success) return NotFound(OperationResult.Fail("Feed not found."));
        return Ok(OperationResult.Ok("Feed deactivated."));
    }
}
