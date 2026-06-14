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
                Status = rc.Status.ToString(),
                CreatedAt = rc.CreatedAt,
                FeedId = rc.Feed.FeedId,
                SuggestedStationId = rc.SuggestedCanonicalStationId,
                SuggestedStationName = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Name : null
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
                Status = rc.Status.ToString(),
                CreatedAt = rc.CreatedAt,
                FeedId = rc.Feed.FeedId,
                SuggestedStationId = rc.SuggestedCanonicalStationId,
                SuggestedStationName = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Name : null
            })
            .ToListAsync(ct);

        return Ok(OperationResult<List<ReconciliationDto>>.Ok(candidates));
    }

    [HttpPost("{id}/approve")]
    public async Task<ActionResult<OperationResult>> Approve(int id, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync(new object[] { id }, ct);
        if (candidate is null) return NotFound(OperationResult.Fail("Candidate not found."));

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
        return Ok(OperationResult.Ok("Approved."));
    }

    [HttpPost("{id}/reject")]
    public async Task<ActionResult<OperationResult>> Reject(int id, [FromQuery] bool createNewStation = false, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync(new object[] { id }, ct);
        if (candidate is null) return NotFound(OperationResult.Fail("Candidate not found."));

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

        return Ok(OperationResult.Ok("Rejected."));
    }

    [HttpPost("{id}/reassign")]
    public async Task<ActionResult<OperationResult>> Reassign(int id, [FromQuery] int canonicalStationId, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates.FindAsync(new object[] { id }, ct);
        if (candidate is null) return NotFound(OperationResult.Fail("Candidate not found."));

        var station = await _db.CanonicalStations.FindAsync(new object[] { canonicalStationId }, ct);
        if (station is null) return NotFound(OperationResult.Fail("Station not found."));

        candidate.SuggestedCanonicalStationId = canonicalStationId;
        candidate.Status = ReconciliationStatus.ManuallyApproved;
        candidate.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(OperationResult.Ok("Reassigned."));
    }
}
