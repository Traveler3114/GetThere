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
    private readonly IConfiguration _config;

    public ReconciliationController(ReconciliationManager ReconciliationManager, TransitDbContext db, IConfiguration config)
    {
        _reconciliationService = ReconciliationManager;
        _db = db;
        _config = config;
    }

    [HttpGet("pending")]
    public async Task<ActionResult<Paginated<ReconciliationDto>>> GetPending(
        [FromQuery] int? feedVersionId = null,
        [FromQuery] string? routeType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var autoNameThreshold = _config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90);
        var autoDistThreshold = _config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100);
        var manualNameThreshold = _config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70);
        var manualDistThreshold = _config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300);

        var query = _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Include(rc => rc.RawStop)
            .Where(rc => rc.Status == ReconciliationStatus.Pending)
            .AsQueryable();

        if (feedVersionId.HasValue)
            query = query.Where(rc => _db.RawStops.Any(rs => rs.Id == rc.RawStopId && rs.FeedVersionId == feedVersionId.Value));
        if (!string.IsNullOrWhiteSpace(routeType))
            query = query.Where(rc => rc.RawRouteType.ToString() == routeType);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<ReconciliationStatus>(status, out var st))
                query = query.Where(rc => rc.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(rc =>
                rc.RawStopName.Contains(q) ||
                rc.Feed.FeedId.Contains(q) ||
                rc.RawStop.RawStopId.Contains(q));

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
                CanonicalRouteType = rc.CanonicalRouteType != null ? rc.CanonicalRouteType.Value.ToString() : null,
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
                SuggestedStationLon = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Longitude : null,
                AutoMergeNameThreshold = autoNameThreshold,
                AutoMergeDistanceMeters = autoDistThreshold,
                ManualReviewNameThreshold = manualNameThreshold,
                ManualReviewDistanceMeters = manualDistThreshold
            })
            .ToListAsync(ct);

        return Ok(new Paginated<ReconciliationDto>(candidates, total, page, perPage));
    }

    [HttpGet("auto-merged")]
    public async Task<ActionResult<Paginated<ReconciliationDto>>> GetAutoMerged(
        [FromQuery] string? routeType = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var autoNameThreshold = _config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90);
        var autoDistThreshold = _config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100);
        var manualNameThreshold = _config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70);
        var manualDistThreshold = _config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300);

        var query = _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Include(rc => rc.RawStop)
            .Where(rc => rc.Status == ReconciliationStatus.AutoMerged)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(routeType))
            query = query.Where(rc => rc.RawRouteType.ToString() == routeType);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(rc =>
                rc.RawStopName.Contains(q) ||
                rc.Feed.FeedId.Contains(q) ||
                rc.RawStop.RawStopId.Contains(q));

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
                CanonicalRouteType = rc.CanonicalRouteType != null ? rc.CanonicalRouteType.Value.ToString() : null,
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
                SuggestedStationLon = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Longitude : null,
                AutoMergeNameThreshold = autoNameThreshold,
                AutoMergeDistanceMeters = autoDistThreshold,
                ManualReviewNameThreshold = manualNameThreshold,
                ManualReviewDistanceMeters = manualDistThreshold
            })
            .ToListAsync(ct);

        return Ok(new Paginated<ReconciliationDto>(candidates, total, page, perPage));
    }

    // TODO: Before Phase 3 (public launch), this endpoint exposes reconciliation
    // candidates for a given station (Task 4.1). Must be restricted to admin-only.
    [HttpGet("by-station/{stationId:int}")]
    public async Task<ActionResult<List<ReconciliationDetailDto>>> GetByStation(int stationId, CancellationToken ct = default)
    {
        var stationExists = await _db.CanonicalStations.AnyAsync(cs => cs.Id == stationId, ct);
        if (!stationExists)
            return NotFound();

        var candidates = await _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .ThenInclude(f => f.Operator)
            .ThenInclude(o => o.Country)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Include(rc => rc.RawStop)
            .Where(rc => rc.SuggestedCanonicalStationId == stationId)
            .OrderBy(rc => rc.CreatedAt)
            .ToListAsync(ct);

        var results = new List<ReconciliationDetailDto>(candidates.Count);
        foreach (var candidate in candidates)
            results.Add(await MapToDetailDto(candidate, ct));

        return Ok(results);
    }

    // TODO: Before Phase 3 (public launch), this endpoint exposes individual
    // reconciliation candidate details (Task 4.1). Must be restricted to admin-only.
    [HttpGet("{id}")]
    public async Task<ActionResult<ReconciliationDetailDto>> GetById(int id, CancellationToken ct = default)
    {
        var candidate = await _db.ReconciliationCandidates
            .Include(rc => rc.Feed)
            .ThenInclude(f => f.Operator)
            .ThenInclude(o => o.Country)
            .Include(rc => rc.SuggestedCanonicalStation)
            .Include(rc => rc.RawStop)
            .FirstOrDefaultAsync(rc => rc.Id == id, ct);

        if (candidate is null)
            return NotFound();

        return Ok(await MapToDetailDto(candidate, ct));
    }

    private async Task<ReconciliationDetailDto> MapToDetailDto(ReconciliationCandidate candidate, CancellationToken ct)
    {
        var autoNameThreshold = (double)(candidate.AutoMergeNameThresholdAtDecision
            ?? (decimal)_config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90));
        var autoDistThreshold = (double)(candidate.AutoMergeDistanceMetersAtDecision
            ?? (decimal)_config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100));
        var manualNameThreshold = (double)(candidate.ManualReviewNameThresholdAtDecision
            ?? (decimal)_config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70));
        var manualDistThreshold = (double)(candidate.ManualReviewDistanceMetersAtDecision
            ?? (decimal)_config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300));

        var normalizedRaw = ReconciliationManager.NormalizeName(candidate.RawStopName);
        var normalizedStation = candidate.SuggestedCanonicalStation != null
            ? ReconciliationManager.NormalizeName(candidate.SuggestedCanonicalStation.Name)
            : null;

        var explanation = ReconciliationManager.ComputeMatchExplanation(
            candidate.NameSimilarityScore, candidate.DistanceMeters,
            candidate.NameMatched, candidate.DistanceMatched, candidate.RouteTypeMatched,
            autoNameThreshold, autoDistThreshold,
            manualNameThreshold, manualDistThreshold);

        StationDetailDto? rawDetail = null;
        if (candidate.RawStop is not null)
        {
            var rawRoutes = await _db.CanonicalRoutes
                .Where(r => _db.StopTimes.Any(st =>
                    st.RawStopEntityId == candidate.RawStopId
                    && st.Trip.CanonicalRouteId == r.Id))
                .Select(r => new RouteInfoDto
                {
                    Id = r.Id,
                    Name = r.LongName,
                    ShortName = r.ShortName,
                    RouteType = r.RouteType.ToString(),
                    OperatorName = r.Operator.Name,
                    OperatorGlobalId = r.Operator.GlobalId
                })
                .ToListAsync(ct);

            var feedOp = candidate.Feed?.Operator;
            var ops = new List<OperatorBriefDto>();
            if (feedOp is not null)
            {
                ops.Add(new OperatorBriefDto
                {
                    GlobalId = feedOp.GlobalId,
                    Name = feedOp.Name,
                    ShortName = feedOp.ShortName,
                    OperatorType = feedOp.OperatorType.ToString()
                });
            }

            rawDetail = new StationDetailDto
            {
                Id = candidate.RawStop.Id,
                Name = candidate.RawStop.Name,
                Latitude = candidate.RawStop.Lat,
                Longitude = candidate.RawStop.Lon,
                RouteType = candidate.RawStop.RouteType?.ToString() ?? "?",
                Operators = ops,
                Routes = rawRoutes
            };
        }

        StationDetailDto? suggestedDetail = null;
        if (candidate.SuggestedCanonicalStationId.HasValue && candidate.SuggestedCanonicalStation is not null)
        {
            var stationId = candidate.SuggestedCanonicalStationId.Value;

            var operators = await _db.CanonicalStationOperators
                .Where(cso => cso.CanonicalStationId == stationId)
                .Select(cso => new OperatorBriefDto
                {
                    GlobalId = cso.Operator.GlobalId,
                    Name = cso.Operator.Name,
                    ShortName = cso.Operator.ShortName,
                    OperatorType = cso.Operator.OperatorType.ToString()
                })
                .ToListAsync(ct);

            var routes = await _db.CanonicalRoutes
                .Where(r => _db.StopTimes.Any(st =>
                    st.CanonicalStationId == stationId
                    && st.Trip.CanonicalRouteId == r.Id))
                .Select(r => new RouteInfoDto
                {
                    Id = r.Id,
                    Name = r.LongName,
                    ShortName = r.ShortName,
                    RouteType = r.RouteType.ToString(),
                    OperatorName = r.Operator.Name,
                    OperatorGlobalId = r.Operator.GlobalId
                })
                .ToListAsync(ct);

            suggestedDetail = new StationDetailDto
            {
                Id = stationId,
                Name = candidate.SuggestedCanonicalStation.Name,
                Latitude = candidate.SuggestedCanonicalStation.Latitude,
                Longitude = candidate.SuggestedCanonicalStation.Longitude,
                RouteType = candidate.SuggestedCanonicalStation.PrimaryRouteType.ToString(),
                Operators = operators,
                Routes = routes
            };
        }

        var verdict = ReconciliationManager.ComputeAutoMergeVerdict(
            candidate.NameSimilarityScore, candidate.DistanceMeters,
            candidate.NameMatched, candidate.DistanceMatched, candidate.RouteTypeMatched,
            candidate.RawRouteType.ToString(), candidate.CanonicalRouteType?.ToString(),
            autoNameThreshold, autoDistThreshold,
            candidate.Status.ToString());

        return new ReconciliationDetailDto
        {
            Id = candidate.Id,
            RawStopId = candidate.RawStopId,
            RawStopName = candidate.RawStopName,
            RawStopLat = candidate.RawStopLat,
            RawStopLon = candidate.RawStopLon,
            RawStopGtfsId = candidate.RawStop?.RawStopId,
            RawRouteType = candidate.RawRouteType.ToString(),
            CanonicalRouteType = candidate.CanonicalRouteType?.ToString(),
            ConfidenceScore = candidate.ConfidenceScore,
            NameSimilarityScore = candidate.NameSimilarityScore,
            DistanceMeters = candidate.DistanceMeters,
            NameMatched = candidate.NameMatched,
            DistanceMatched = candidate.DistanceMatched,
            RouteTypeMatched = candidate.RouteTypeMatched,
            AutoReconciled = candidate.AutoReconciled,
            Status = candidate.Status.ToString(),
            CreatedAt = candidate.CreatedAt,
            ReviewedAt = candidate.ReviewedAt,
            ReviewedByAdminId = candidate.ReviewedByAdminId,
            FeedId = candidate.Feed?.FeedId,
            SuggestedStationId = candidate.SuggestedCanonicalStationId,
            SuggestedStationName = candidate.SuggestedCanonicalStation?.Name,
            SuggestedStationLat = candidate.SuggestedCanonicalStation?.Latitude,
            SuggestedStationLon = candidate.SuggestedCanonicalStation?.Longitude,
            NormalizedRawName = normalizedRaw,
            NormalizedStationName = normalizedStation,
            MatchExplanation = explanation,
            AutoMergeNameThreshold = autoNameThreshold,
            AutoMergeDistanceMeters = autoDistThreshold,
            ManualReviewNameThreshold = manualNameThreshold,
            ManualReviewDistanceMeters = manualDistThreshold,
            RawStopCountry = candidate.Feed?.Operator?.Country?.Name,
            RawStopDetail = rawDetail,
            SuggestedStationDetail = suggestedDetail,
            AutoMergeVerdict = verdict
        };
    }

    // TODO: Before Phase 3 (public launch), this endpoint exposes station split
    // audit records (Task 4.18). Must be restricted to admin-only.
    [HttpGet("split-log")]
    public async Task<ActionResult<List<StationSplitLogDto>>> GetSplitLog([FromQuery] int candidateStationId, CancellationToken ct = default)
    {
        var logs = await _db.StationSplitLogs
            .Where(sl => sl.CandidateStationId == candidateStationId)
            .OrderBy(sl => sl.CreatedAt)
            .Select(sl => new StationSplitLogDto
            {
                Id = sl.Id,
                RawStopId = sl.RawStopId,
                FeedVersionId = sl.FeedVersionId,
                CandidateStationId = sl.CandidateStationId,
                Reason = sl.Reason,
                Detail = sl.Detail,
                CreatedAt = sl.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(logs);
    }

    // TODO: Before Phase 3 (public launch), this endpoint allows approving
    // reconciliation candidates (Task 4.11). Must be restricted to admin-only.
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct = default)
    {
        await _reconciliationService.ApproveCandidateAsync(id, ct);
        return NoContent();
    }

    // TODO: Before Phase 3 (public launch), this endpoint allows rejecting
    // reconciliation candidates (Task 4.11). Must be restricted to admin-only.
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(int id, [FromQuery] bool createNewStation = false, CancellationToken ct = default)
    {
        await _reconciliationService.RejectCandidateAsync(id, createNewStation, ct);
        return NoContent();
    }

    // TODO: Before Phase 3 (public launch), this endpoint exposes station merge
    // audit records (Task 4.15). Must be restricted to admin-only.
    [HttpGet("merge-log")]
    public async Task<ActionResult<List<StationMergeLogDto>>> GetMergeLog(CancellationToken ct = default)
    {
        var logs = await _db.StationMergeLogs
            .OrderByDescending(ml => ml.MergedAt)
            .Select(ml => new StationMergeLogDto
            {
                Id = ml.Id,
                SourceStationId = ml.SourceStationId,
                SourceStationName = ml.Source.Name,
                SourceStationGlobalId = ml.SourceStationGlobalId,
                TargetStationId = ml.TargetStationId,
                TargetStationName = ml.Target.Name,
                RawStopsMovedCount = ml.RawStopsMovedCount,
                MergedAt = ml.MergedAt,
                Unmerged = ml.Source.IsActive
            })
            .ToListAsync(ct);

        return Ok(logs);
    }

    [HttpGet("merge-preview")]
    public async Task<ActionResult<object>> MergePreview([FromQuery] int stationAId, [FromQuery] int stationBId, CancellationToken ct = default)
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

    // TODO: Before Phase 3 (public launch), this endpoint checks route-set and
    // direction conflicts between two stations before merge/reassign (Task 4.16).
    // Must be restricted to admin-only.
    [HttpGet("check-action-warning")]
    public async Task<ActionResult<object>> CheckActionWarning([FromQuery] int stationAId, [FromQuery] int stationBId, CancellationToken ct = default)
    {
        var warning = await _reconciliationService.CheckManualActionWarningAsync(stationAId, stationBId, ct);
        return Ok(new { warning });
    }

    // TODO: Before Phase 3 (public launch), this endpoint reassigns a
    // reconciliation candidate to a different station (Task 4.13). Must be
    // restricted to admin-only.
    [HttpPost("{id}/reassign")]
    public async Task<IActionResult> Reassign(int id, [FromQuery] int canonicalStationId, CancellationToken ct = default)
    {
        var warning = await _reconciliationService.ReassignCandidateAsync(id, canonicalStationId, ct);
        if (warning is not null)
            return Ok(new { warning });
        return NoContent();
    }

    // TODO: Before Phase 3 (public launch), this endpoint merges two stations
    // and deactivates the source (Task 4.12). Must be restricted to admin-only.
    [HttpPost("merge-stations")]
    public async Task<IActionResult> MergeStations([FromQuery] int sourceStationId, [FromQuery] int targetStationId, CancellationToken ct = default)
    {
        await _reconciliationService.MergeStationsAsync(sourceStationId, targetStationId, ct);
        return NoContent();
    }
}
