using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("reconciliation")]
public class ReconciliationController : ControllerBase
{
    private readonly ReconciliationManager _reconciliationService;
    private readonly TransitDbContext _db;

    public ReconciliationController(ReconciliationManager ReconciliationManager, TransitDbContext db)
    {
        _reconciliationService = ReconciliationManager;
        _db = db;
    }

    [HttpGet("pending")]
    public async Task<ActionResult<OperationResult<List<ReconciliationDto>>>> GetPending(
        [FromQuery] int? feedVersionId = null,
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Include(rc => rc.RawStop)
            .Where(rc => rc.Status == ReconciliationStatus.Pending)
            .AsQueryable();

        if (feedVersionId.HasValue)
            query = query.Where(rc => rc.RawStop.FeedVersionId == feedVersionId.Value);

        if (after > 0)
            query = query.Where(rc => rc.Id > after);

        var candidates = await query
            .OrderBy(rc => rc.Id)
            .Take(perPage)
            .Select(rc => new ReconciliationDto
            {
                Id = rc.Id,
                RawStopId = rc.RawStopId,
                RawStopName = rc.RawStopName,
                RawStopLat = rc.RawStopLat,
                RawStopLon = rc.RawStopLon,
                RawStopGtfsId = rc.RawStop.RawStopId,
                RawRouteType = rc.RawRouteType.ToString(),
                CanonicalRouteType = rc.CanonicalRouteType.ToString(),
                ConfidenceScore = rc.ConfidenceScore,
                NameSimilarityScore = rc.NameSimilarityScore,
                DistanceMeters = rc.DistanceMeters,
                NameMatched = rc.NameMatched,
                DistanceMatched = rc.DistanceMatched,
                RouteTypeMatched = rc.RouteTypeMatched,
                AutoReconciled = rc.AutoReconciled,
                Status = rc.Status.ToString(),
                CreatedAt = rc.CreatedAt,
                FeedId = rc.Feed.FeedId,
                SuggestedStationId = rc.SuggestedCanonicalStationId,
                SuggestedStationName = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Name : null,
                SuggestedStationLat = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Latitude : null,
                SuggestedStationLon = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Longitude : null
            })
            .ToListAsync(ct);

        var nextAfter = candidates.Count > 0 ? candidates.Last().Id : after;
        var total = await _db.ReconciliationCandidates.CountAsync(rc => rc.Status == ReconciliationStatus.Pending, ct);
        var nextUrl = candidates.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<ReconciliationDto>>.OkPaginated(candidates, nextAfter, total, nextUrl));
    }

    [HttpGet("auto-merged")]
    public async Task<ActionResult<OperationResult<List<ReconciliationDto>>>> GetAutoMerged(
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Include(rc => rc.RawStop)
            .Where(rc => rc.Status == ReconciliationStatus.AutoMerged)
            .AsQueryable();

        if (after > 0)
            query = query.Where(rc => rc.Id > after);

        var candidates = await query
            .OrderBy(rc => rc.Id)
            .Take(perPage)
            .Select(rc => new ReconciliationDto
            {
                Id = rc.Id,
                RawStopId = rc.RawStopId,
                RawStopName = rc.RawStopName,
                RawStopLat = rc.RawStopLat,
                RawStopLon = rc.RawStopLon,
                RawStopGtfsId = rc.RawStop.RawStopId,
                RawRouteType = rc.RawRouteType.ToString(),
                CanonicalRouteType = rc.CanonicalRouteType.ToString(),
                ConfidenceScore = rc.ConfidenceScore,
                NameSimilarityScore = rc.NameSimilarityScore,
                DistanceMeters = rc.DistanceMeters,
                NameMatched = rc.NameMatched,
                DistanceMatched = rc.DistanceMatched,
                RouteTypeMatched = rc.RouteTypeMatched,
                AutoReconciled = rc.AutoReconciled,
                Status = rc.Status.ToString(),
                CreatedAt = rc.CreatedAt,
                FeedId = rc.Feed.FeedId,
                SuggestedStationId = rc.SuggestedCanonicalStationId,
                SuggestedStationName = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Name : null,
                SuggestedStationLat = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Latitude : null,
                SuggestedStationLon = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Longitude : null
            })
            .ToListAsync(ct);

        var nextAfter = candidates.Count > 0 ? candidates.Last().Id : after;
        var total = await _db.ReconciliationCandidates.CountAsync(rc => rc.Status == ReconciliationStatus.AutoMerged, ct);
        var nextUrl = candidates.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<ReconciliationDto>>.OkPaginated(candidates, nextAfter, total, nextUrl));
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
