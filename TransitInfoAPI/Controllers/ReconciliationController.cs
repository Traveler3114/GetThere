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
[Route("reconciliation")]
public class ReconciliationController : ControllerBase
{
    private readonly ReconciliationService _reconciliationService;
    private readonly TransitDbContext _db;

    public ReconciliationController(ReconciliationService reconciliationService, TransitDbContext db)
    {
        _reconciliationService = reconciliationService;
        _db = db;
    }

    [HttpGet("pending")]
    public async Task<ActionResult<OperationResult<List<ReconciliationDto>>>> GetPending(CancellationToken ct = default)
    {
        var candidates = await _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Where(rc => rc.Status == ReconciliationStatus.Pending)
            .OrderByDescending(rc => rc.ConfidenceScore)
            .Select(rc => new ReconciliationDto
            {
                Id = rc.Id,
                RawStopId = rc.RawStopId,
                RawStopName = rc.RawStopName,
                RawStopLat = rc.RawStopLat,
                RawStopLon = rc.RawStopLon,
                ConfidenceScore = rc.ConfidenceScore,
                NameSimilarityScore = rc.NameSimilarityScore,
                DistanceMeters = rc.DistanceMeters,
                Status = rc.Status.ToString(),
                CreatedAt = rc.CreatedAt,
                FeedId = rc.Feed.FeedId,
                SuggestedStationId = rc.SuggestedCanonicalStationId,
                SuggestedStationName = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Name : null,
                SuggestedStationLat = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Latitude : null,
                SuggestedStationLon = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Longitude : null
            })
            .ToListAsync(ct);

        return Ok(OperationResult<List<ReconciliationDto>>.Ok(candidates));
    }

    [HttpGet("auto-merged")]
    public async Task<ActionResult<OperationResult<List<ReconciliationDto>>>> GetAutoMerged(CancellationToken ct = default)
    {
        var candidates = await _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Where(rc => rc.Status == ReconciliationStatus.AutoMerged)
            .OrderByDescending(rc => rc.CreatedAt)
            .Take(50)
            .Select(rc => new ReconciliationDto
            {
                Id = rc.Id,
                RawStopId = rc.RawStopId,
                RawStopName = rc.RawStopName,
                RawStopLat = rc.RawStopLat,
                RawStopLon = rc.RawStopLon,
                ConfidenceScore = rc.ConfidenceScore,
                NameSimilarityScore = rc.NameSimilarityScore,
                DistanceMeters = rc.DistanceMeters,
                Status = rc.Status.ToString(),
                CreatedAt = rc.CreatedAt,
                FeedId = rc.Feed.FeedId,
                SuggestedStationId = rc.SuggestedCanonicalStationId,
                SuggestedStationName = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Name : null,
                SuggestedStationLat = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Latitude : null,
                SuggestedStationLon = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Longitude : null
            })
            .ToListAsync(ct);

        return Ok(OperationResult<List<ReconciliationDto>>.Ok(candidates));
    }

    [HttpPost("{id}/approve")]
    public async Task<ActionResult<OperationResult>> Approve(int id, CancellationToken ct = default)
    {
        var result = await _reconciliationService.ApproveCandidateAsync(id, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost("{id}/reject")]
    public async Task<ActionResult<OperationResult>> Reject(int id, [FromQuery] bool createNewStation = false, CancellationToken ct = default)
    {
        var result = await _reconciliationService.RejectCandidateAsync(id, createNewStation, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost("{id}/reassign")]
    public async Task<ActionResult<OperationResult>> Reassign(int id, [FromQuery] int canonicalStationId, CancellationToken ct = default)
    {
        var result = await _reconciliationService.ReassignCandidateAsync(id, canonicalStationId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }
}
