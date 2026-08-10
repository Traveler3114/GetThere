using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class FeedsController : ControllerBase
{
    private readonly FeedManager _feedService;

    public FeedsController(FeedManager feedManager) { _feedService = feedManager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.FeedsView)]
    public async Task<ActionResult<Paginated<FeedResponse>>> GetAll(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] bool showInternal = false,
        CancellationToken ct = default)
    {
        var (feeds, total) = await _feedService.GetAllAsync(page, perPage, showInternal, ct);
        return Ok(new Paginated<FeedResponse>(feeds, total, page, perPage));
    }

    [HttpPost]
    [Authorize(Policy = PermissionKeys.FeedsManage)]
    public async Task<ActionResult<FeedResponse>> Create(
        [FromBody] CreateFeedRequest request,
        CancellationToken ct = default)
    {
        var feed = await _feedService.CreateAsync(request.OperatorId, request.FeedType, request.FeedId, request.Url, request.RefreshIntervalSeconds, ct, request.CustomSourceId);
        var dto = await _feedService.GetByIdAsync(feed.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { }, dto);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = PermissionKeys.FeedsManage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFeedRequest request, CancellationToken ct = default)
    {
        var (success, message) = await _feedService.UpdateAsync(id, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = PermissionKeys.FeedsManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var success = await _feedService.DeleteAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpGet("versions/{versionId}/logs")]
    [Authorize(Policy = PermissionKeys.FeedVersionsView)]
    public ActionResult<List<string>> GetVersionLogs(int versionId, CancellationToken ct = default)
    {
        var logs = _feedService.GetImportLogs(versionId);
        return Ok(logs);
    }

    [HttpPost("{id}/fetch")]
    [Authorize(Policy = PermissionKeys.FeedsManage)]
    public async Task<ActionResult> Fetch(int id, CancellationToken ct = default)
    {
        var version = await _feedService.TriggerImportAsync(id, ct);
        return Ok(new { message = $"Import succeeded: {version.RouteCount} routes, {version.StopCount} stops, {version.TripCount} trips." });
    }

    /// <summary>
    /// Re-runs station reconciliation for an already-imported version.
    /// <para>
    /// Reconciliation runs after the import transaction commits, so a failure there leaves the
    /// version's data intact but its stops unlinked from canonical stations — recorded as
    /// <c>ReconciliationPending</c>. This is the repair: it is idempotent, so it is also safe to run
    /// against a version that already reconciled cleanly.
    /// </para>
    /// </summary>
    [HttpPost("versions/{versionId}/reconcile")]
    [Authorize(Policy = PermissionKeys.FeedsManage)]
    public async Task<ActionResult> ReconcileVersion(int versionId, CancellationToken ct = default)
    {
        var reconciled = await _feedService.ReconcileImportedVersionAsync(versionId, ct);

        return reconciled
            ? Ok(new { message = "Reconciliation complete." })
            : StatusCode(500, new { message = "Reconciliation failed — the imported data was kept. See the version's import log." });
    }

    [HttpGet("{id}/versions")]
    [Authorize(Policy = PermissionKeys.FeedVersionsView)]
    public async Task<ActionResult<List<FeedVersionResponse>>> GetVersions(int id, CancellationToken ct = default)
    {
        var versions = await _feedService.GetFeedVersionsAsync(id, ct);
        var dtos = versions.Select(FeedVersionMapper.ToResponse).ToList();
        return Ok(dtos);
    }
}
