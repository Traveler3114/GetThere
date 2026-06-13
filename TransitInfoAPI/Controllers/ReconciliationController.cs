using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("reconciliation")]
public class ReconciliationController : ControllerBase
{
    private readonly TransitDbContext _db;

    public ReconciliationController(TransitDbContext db) { _db = db; }

    [HttpGet("pending")]
    public async Task<ActionResult<List<ReconciliationCandidate>>> GetPending(CancellationToken ct = default)
    {
        return await _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Where(rc => rc.Status == ReconciliationStatus.Pending)
            .OrderByDescending(rc => rc.ConfidenceScore)
            .ToListAsync(ct);
    }

    [HttpGet("auto-merged")]
    public async Task<ActionResult<List<ReconciliationCandidate>>> GetAutoMerged(CancellationToken ct = default)
    {
        return await _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Where(rc => rc.Status == ReconciliationStatus.AutoMerged)
            .OrderByDescending(rc => rc.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
    }

    [HttpPost("{id}/approve")]
    public async Task<ActionResult> Approve(int id, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync(new object[] { id }, ct);
        if (candidate is null) return NotFound();

        candidate.Status = ReconciliationStatus.ManuallyApproved;
        candidate.ReviewedAt = DateTime.UtcNow;

        if (candidate.SuggestedCanonicalStationId.HasValue)
        {
            var existing = await _db.CanonicalStationOperators
                .FirstOrDefaultAsync(cso =>
                    cso.CanonicalStationId == candidate.SuggestedCanonicalStationId.Value &&
                    cso.OperatorId == candidate.Feed.OperatorId, ct);

            if (existing is null)
            {
                _db.CanonicalStationOperators.Add(new CanonicalStationOperator
                {
                    CanonicalStationId = candidate.SuggestedCanonicalStationId.Value,
                    OperatorId = candidate.Feed.OperatorId
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id}/reject")]
    public async Task<ActionResult> Reject(int id, [FromQuery] bool createNewStation = false, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync(new object[] { id }, ct);
        if (candidate is null) return NotFound();

        if (createNewStation)
        {
            var newStation = new CanonicalStation
            {
                GlobalId = $"gt-{candidate.Feed.FeedId}-{candidate.RawStopId.ToLowerInvariant()}",
                Name = candidate.RawStopName,
                Latitude = candidate.RawStopLat,
                Longitude = candidate.RawStopLon,
                StationType = StationType.Stop,
                IsActive = true,
                CountryId = 1,
                CreatedAt = DateTime.UtcNow
            };

            _db.CanonicalStations.Add(newStation);
            await _db.SaveChangesAsync(ct);

            candidate.SuggestedCanonicalStationId = newStation.Id;
        }

        candidate.Status = ReconciliationStatus.Rejected;
        candidate.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id}/reassign")]
    public async Task<ActionResult> Reassign(int id, [FromQuery] int canonicalStationId, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync(new object[] { id }, ct);
        if (candidate is null) return NotFound();

        var station = await _db.CanonicalStations.FindAsync(new object[] { canonicalStationId }, ct);
        if (station is null) return NotFound();

        candidate.SuggestedCanonicalStationId = canonicalStationId;
        candidate.Status = ReconciliationStatus.ManuallyApproved;
        candidate.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
