using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<ActionResult<Paginated<ReconciliationDto>>> GetPending(
        [FromQuery] int? feedVersionId = null,
        [FromQuery] int page = 1,
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

        var total = await query.CountAsync(ct);
        var candidates = await query
            .OrderBy(rc => rc.Id)
            .Skip((page - 1) * perPage)
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

        return Ok(new Paginated<ReconciliationDto>(candidates, total));
    }

    [HttpGet("auto-merged")]
    public async Task<ActionResult<Paginated<ReconciliationDto>>> GetAutoMerged(
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Include(rc => rc.RawStop)
            .Where(rc => rc.Status == ReconciliationStatus.AutoMerged)
            .AsQueryable();

        var total = await query.CountAsync(ct);
        var candidates = await query
            .OrderBy(rc => rc.Id)
            .Skip((page - 1) * perPage)
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

        return Ok(new Paginated<ReconciliationDto>(candidates, total));
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

    [HttpPost("{id}/reassign")]
    public async Task<IActionResult> Reassign(int id, [FromQuery] int canonicalStationId, CancellationToken ct = default)
    {
        await _reconciliationService.ReassignCandidateAsync(id, canonicalStationId, ct);
        return NoContent();
    }
}
