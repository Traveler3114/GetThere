using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(Roles = "Admin")]
public class CustomFeedsController : ControllerBase
{
    private readonly CustomFeedManager _manager;

public CustomFeedsController(CustomFeedManager manager) { _manager = manager; }

    [HttpGet]
    public async Task<ActionResult<Paginated<CustomFeedResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var feeds = await _manager.GetAllAsync(page, perPage, ct);
        var total = await _manager.GetTotalCountAsync(ct);
        return Ok(new Paginated<CustomFeedResponse>(feeds, total, page, perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomFeedResponse>> GetById(int id, CancellationToken ct = default)
    {
        var feed = await _manager.GetByIdAsync(id, ct);
        if (feed is null) return NotFound();
        return Ok(feed);
    }

    [HttpPost]
    public async Task<ActionResult<CustomFeedResponse>> Create([FromBody] CreateCustomFeedRequest request, CancellationToken ct = default)
    {
        var feed = await _manager.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = feed.Id }, feed);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomFeedRequest request, CancellationToken ct = default)
    {
        var success = await _manager.UpdateAsync(id, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var success = await _manager.DeleteAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpGet("{feedId:int}/runs")]
    public async Task<ActionResult<Paginated<CustomFeedRunResponse>>> GetRuns(
        int feedId,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var runs = await _manager.GetRunsAsync(feedId, page, perPage, ct);
        var total = await _manager.GetRunsTotalCountAsync(feedId, ct);
        return Ok(new Paginated<CustomFeedRunResponse>(runs, total, page, perPage));
    }

    [HttpGet("{feedId:int}/runs/{runId:int}")]
    public async Task<ActionResult<CustomFeedRunResponse>> GetRun(int feedId, int runId, CancellationToken ct = default)
    {
        var run = await _manager.GetRunByIdAsync(runId, ct);
        if (run is null) return NotFound();
        return Ok(run);
    }

    [HttpPost("{id:int}/execute")]
    public async Task<ActionResult<CustomFeedRunResponse>> Execute(int id, CancellationToken ct = default)
    {
        var run = await _manager.ExecuteAsync(id, ct);
        return CreatedAtAction(nameof(GetRun), new { feedId = id, runId = run.Id }, run);
    }

    [HttpGet("discover")]
    public async Task<ActionResult<CustomFeedDiscoverResponse>> Discover([FromBody] CreateCustomFeedRequest request, CancellationToken ct = default)
    {
        var result = await _manager.DiscoverAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("preview")]
    public async Task<ActionResult<CustomFeedPreviewResponse>> Preview([FromBody] CreateCustomFeedRequest request, CancellationToken ct = default)
    {
        var result = await _manager.PreviewAsync(request, ct);
        return Ok(result);
    }
}