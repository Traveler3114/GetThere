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
                SuggestedStationLon = rc.SuggestedCanonicalStation != null ? rc.SuggestedCanonicalStation.Longitude : null,
                AutoMergeNameThreshold = autoNameThreshold,
                AutoMergeDistanceMeters = autoDistThreshold,
                ManualReviewNameThreshold = manualNameThreshold,
                ManualReviewDistanceMeters = manualDistThreshold
            })
            .ToListAsync(ct);

        return Ok(new Paginated<ReconciliationDto>(candidates, total, page, perPage));
    }

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

        var autoNameThreshold = _config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90);
        var autoDistThreshold = _config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100);
        var manualNameThreshold = _config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70);
        var manualDistThreshold = _config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300);

        var normalizedRaw = ReconciliationManager.NormalizeName(candidate.RawStopName);
        var normalizedStation = candidate.SuggestedCanonicalStation != null
            ? ReconciliationManager.NormalizeName(candidate.SuggestedCanonicalStation.Name)
            : null;

        var explanation = ReconciliationManager.ComputeMatchExplanation(
            candidate.NameSimilarityScore, candidate.DistanceMeters,
            candidate.NameMatched, candidate.DistanceMatched, candidate.RouteTypeMatched,
            autoNameThreshold, autoDistThreshold,
            manualNameThreshold, manualDistThreshold);

        // Fetch raw stop route detail
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

        // Fetch suggested station detail
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

        var dto = new ReconciliationDetailDto
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

        return Ok(dto);
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
