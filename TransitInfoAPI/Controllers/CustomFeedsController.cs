using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("custom-feeds")]
public class CustomFeedsController : ControllerBase
{
    private readonly CustomFeedManager _manager;
    private readonly ILogger<CustomFeedsController> _logger;

    public CustomFeedsController(CustomFeedManager manager, ILogger<CustomFeedsController> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<Paginated<Contracts.CustomFeedResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var feeds = await _manager.GetAllAsync(page, perPage, ct);
        var total = await _manager.GetTotalCountAsync(ct);
        return Ok(new Paginated<Contracts.CustomFeedResponse>(feeds, total, page, perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Contracts.CustomFeedResponse>> GetById(int id, CancellationToken ct = default)
    {
        var feed = await _manager.GetByIdAsync(id, ct);
        if (feed is null) return NotFound();
        return Ok(feed);
    }

    [HttpPost]
    public async Task<ActionResult<Contracts.CustomFeedResponse>> Create(
        [FromBody] Contracts.CreateCustomFeedRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DataPath))
            return Problem(statusCode: 400, title: "DataPath is required.");
        if (!Enum.TryParse<Enums.ResponseFormat>(request.ResponseFormat, true, out _))
            return Problem(statusCode: 400, title: $"Invalid ResponseFormat '{request.ResponseFormat}'.");
        if (!Enum.TryParse<Enums.OutputFormat>(request.OutputFormat, true, out _))
            return Problem(statusCode: 400, title: $"Invalid OutputFormat '{request.OutputFormat}'.");

        foreach (var m in request.FieldMappings)
        {
            if (!Enum.TryParse<Enums.MappingKind>(m.MappingKind, true, out _))
                return Problem(statusCode: 400, title: $"Invalid MappingKind '{m.MappingKind}'.");
        }

        var feed = await _manager.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), null, feed);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] Contracts.UpdateCustomFeedRequest request, CancellationToken ct = default)
    {
        if (request.ResponseFormat is not null &&
            !Enum.TryParse<Enums.ResponseFormat>(request.ResponseFormat, true, out _))
            return Problem(statusCode: 400, title: $"Invalid ResponseFormat '{request.ResponseFormat}'.");
        if (request.OutputFormat is not null &&
            !Enum.TryParse<Enums.OutputFormat>(request.OutputFormat, true, out _))
            return Problem(statusCode: 400, title: $"Invalid OutputFormat '{request.OutputFormat}'.");

        if (request.FieldMappings is not null)
        {
            foreach (var m in request.FieldMappings)
            {
                if (!Enum.TryParse<Enums.MappingKind>(m.MappingKind, true, out _))
                    return Problem(statusCode: 400, title: $"Invalid MappingKind '{m.MappingKind}'.");
            }
        }

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

    [HttpPost("{id:int}/execute")]
    public async Task<ActionResult<Contracts.CustomFeedRunResponse>> Execute(int id, CancellationToken ct = default)
    {
        try
        {
            var run = await _manager.ExecuteAsync(id, ct);
            return Ok(run);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { title = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom feed {Id} execute failed", id);
            return Problem(statusCode: 500, title: "Execution failed", detail: ex.Message);
        }
    }

    [HttpGet("{id:int}/runs")]
    public async Task<ActionResult<Paginated<Contracts.CustomFeedRunResponse>>> GetRuns(
        int id,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var runs = await _manager.GetRunsAsync(id, page, perPage, ct);
        var total = await _manager.GetRunsTotalCountAsync(id, ct);
        return Ok(new Paginated<Contracts.CustomFeedRunResponse>(runs, total, page, perPage));
    }

    [HttpGet("runs/{runId:int}")]
    public async Task<ActionResult<Contracts.CustomFeedRunResponse>> GetRunDetail(int runId, CancellationToken ct = default)
    {
        var run = await _manager.GetRunByIdAsync(runId, ct);
        if (run is null) return NotFound();
        return Ok(run);
    }

    [HttpPost("discover")]
    public async Task<ActionResult<Contracts.CustomFeedDiscoverResponse>> Discover(
        [FromBody] Contracts.CreateCustomFeedRequest request,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<Enums.ResponseFormat>(request.ResponseFormat, true, out _))
            return Problem(statusCode: 400, title: $"Invalid ResponseFormat '{request.ResponseFormat}'.");

        try
        {
            var result = await _manager.DiscoverAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discover failed");
            return Problem(statusCode: 400, title: "Discover failed", detail: ex.Message);
        }
    }

    [HttpPost("preview")]
    public async Task<ActionResult<Contracts.CustomFeedPreviewResponse>> Preview(
        [FromBody] Contracts.CreateCustomFeedRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DataPath))
            return Problem(statusCode: 400, title: "DataPath is required for preview.");
        if (!Enum.TryParse<Enums.ResponseFormat>(request.ResponseFormat, true, out _))
            return Problem(statusCode: 400, title: $"Invalid ResponseFormat '{request.ResponseFormat}'.");
        if (!Enum.TryParse<Enums.OutputFormat>(request.OutputFormat, true, out _))
            return Problem(statusCode: 400, title: $"Invalid OutputFormat '{request.OutputFormat}'.");

        try
        {
            var result = await _manager.PreviewAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview failed");
            return Problem(statusCode: 400, title: "Preview failed", detail: ex.Message);
        }
    }
}
