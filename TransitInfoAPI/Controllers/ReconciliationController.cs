using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("reconciliation")]
[Authorize(Roles = "Admin")]
public class ReconciliationController : ControllerBase
{
    private readonly ReconciliationManager _reconciliationService;

public ReconciliationController(ReconciliationManager reconciliationManager) { _reconciliationService = reconciliationManager; }

    [HttpGet("pending")]
    public async Task<ActionResult<Paginated<ReconciliationResponse>>> GetPending(
        [FromQuery] int? feedVersionId = null,
        [FromQuery] string? routeType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var (items, total) = await _reconciliationService.GetPendingAsync(feedVersionId, routeType, status, q, page, perPage, ct);
        return Ok(new Paginated<ReconciliationResponse>(items, total, page, perPage));
    }

    [HttpGet("auto-merged")]
    public async Task<ActionResult<Paginated<ReconciliationResponse>>> GetAutoMerged(
        [FromQuery] string? routeType = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var (items, total) = await _reconciliationService.GetAutoMergedAsync(routeType, q, page, perPage, ct);
        return Ok(new Paginated<ReconciliationResponse>(items, total, page, perPage));
    }

    [HttpGet("by-station/{stationId:int}")]
    public async Task<ActionResult<List<ReconciliationDetailResponse>>> GetByStation(int stationId, CancellationToken ct = default)
    {
        var results = await _reconciliationService.GetByStationAsync(stationId, ct);
        if (results is null) return NotFound();
        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ReconciliationDetailResponse>> GetById(int id, CancellationToken ct = default)
    {
        var result = await _reconciliationService.GetByIdAsync(id, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpGet("split-log")]
    public async Task<ActionResult<List<StationSplitLogResponse>>> GetSplitLog([FromQuery] int candidateStationId, CancellationToken ct = default)
    {
        var logs = await _reconciliationService.GetSplitLogAsync(candidateStationId, ct);
        return Ok(logs);
    }

    [HttpGet("merge-log")]
    public async Task<ActionResult<List<StationMergeLogResponse>>> GetMergeLog(CancellationToken ct = default)
    {
        var logs = await _reconciliationService.GetMergeLogAsync(ct);
        return Ok(logs);
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct = default)
    {
        await _reconciliationService.ApproveCandidateAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(int id, [FromQuery] bool createNewStation = false, CancellationToken ct = default)
    {
        await _reconciliationService.RejectCandidateAsync(id, createNewStation, ct);
        return NoContent();
    }

    [HttpGet("merge-preview")]
    public async Task<ActionResult<MergePreviewResponse>> MergePreview([FromQuery] int stationAId, [FromQuery] int stationBId, CancellationToken ct = default)
    {
        var preview = await _reconciliationService.GetMergePreviewAsync(stationAId, stationBId, ct);
        return Ok(preview);
    }

    [HttpPost("unmerge/{mergeLogId}")]
    public async Task<IActionResult> Unmerge(int mergeLogId, CancellationToken ct = default)
    {
        await _reconciliationService.UnmergeStationsAsync(mergeLogId, ct);
        return NoContent();
    }

    [HttpGet("check-action-warning")]
    public async Task<ActionResult<object>> CheckActionWarning([FromQuery] int stationAId, [FromQuery] int stationBId, CancellationToken ct = default)
    {
        var warning = await _reconciliationService.CheckManualActionWarningAsync(stationAId, stationBId, ct);
        return Ok(new { warning });
    }

    [HttpPost("{id}/reassign")]
    public async Task<IActionResult> Reassign(int id, [FromQuery] int canonicalStationId, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        var warning = await _reconciliationService.ReassignCandidateAsync(id, canonicalStationId, force, ct);
        if (warning is not null)
            return Ok(new { warning });
        return NoContent();
    }

    [HttpPost("merge-stations")]
    public async Task<IActionResult> MergeStations([FromQuery] int sourceStationId, [FromQuery] int targetStationId, CancellationToken ct = default)
    {
        await _reconciliationService.MergeStationsAsync(sourceStationId, targetStationId, ct);
        return NoContent();
    }
}