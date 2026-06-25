using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Mapping;

public static class ReconciliationMapper
{
    public static ReconciliationResponse ToResponse(ReconciliationCandidate rc, string? feedId, string? suggestedStationName, double? suggestedStationLat, double? suggestedStationLon) => new()
    {
        Id = rc.Id,
        RawStopId = rc.RawStopId,
        RawStopName = rc.RawStopName,
        RawStopLat = rc.RawStopLat,
        RawStopLon = rc.RawStopLon,
        RawStopGtfsId = rc.RawStop?.RawStopId,
        RawRouteType = rc.RawRouteType.ToString(),
        CanonicalRouteType = rc.CanonicalRouteType?.ToString(),
        ConfidenceScore = rc.ConfidenceScore,
        NameSimilarityScore = rc.NameSimilarityScore,
        DistanceMeters = rc.DistanceMeters,
        NameMatched = rc.NameMatched,
        DistanceMatched = rc.DistanceMatched,
        RouteTypeMatched = rc.RouteTypeMatched,
        AutoReconciled = rc.AutoReconciled,
        Status = rc.Status.ToString(),
        CreatedAt = rc.CreatedAt,
        FeedId = feedId,
        SuggestedStationId = rc.SuggestedCanonicalStationId,
        SuggestedStationName = suggestedStationName,
        SuggestedStationLat = suggestedStationLat,
        SuggestedStationLon = suggestedStationLon
    };

    public static async Task<ReconciliationDetailResponse> ToDetailResponse(
        ReconciliationCandidate candidate, TransitDbContext db, IConfiguration config, CancellationToken ct)
    {
        var autoNameThreshold = (double)(candidate.AutoMergeNameThresholdAtDecision
            ?? (decimal)config.GetValue<double>("Reconciliation:AutoMergeNameThreshold", 0.90));
        var autoDistThreshold = (double)(candidate.AutoMergeDistanceMetersAtDecision
            ?? (decimal)config.GetValue<double>("Reconciliation:AutoMergeDistanceMeters", 100));
        var manualNameThreshold = (double)(candidate.ManualReviewNameThresholdAtDecision
            ?? (decimal)config.GetValue<double>("Reconciliation:ManualReviewNameThreshold", 0.70));
        var manualDistThreshold = (double)(candidate.ManualReviewDistanceMetersAtDecision
            ?? (decimal)config.GetValue<double>("Reconciliation:ManualReviewDistanceMeters", 300));

        var normalizedRaw = ReconciliationManager.NormalizeName(candidate.RawStopName);
        var normalizedStation = candidate.SuggestedCanonicalStation != null
            ? ReconciliationManager.NormalizeName(candidate.SuggestedCanonicalStation.Name)
            : null;

        var explanation = ReconciliationManager.ComputeMatchExplanation(
            candidate.NameSimilarityScore, candidate.DistanceMeters,
            candidate.NameMatched, candidate.DistanceMatched, candidate.RouteTypeMatched,
            autoNameThreshold, autoDistThreshold,
            manualNameThreshold, manualDistThreshold);

        StationDetailResponse? rawDetail = null;
        if (candidate.RawStop is not null)
        {
            var rawRouteEntities = await db.CanonicalRoutes
                .Include(r => r.Operator)
                .Where(r => db.StopTimes.Any(st =>
                    st.RawStopEntityId == candidate.RawStopId
                    && st.Trip.CanonicalRouteId == r.Id))
                .ToListAsync(ct);
            var rawRoutes = rawRouteEntities.Select(RouteMapper.ToInfoResponse).ToList();

            var feedOp = candidate.Feed?.Operator;
            var ops = new List<OperatorBriefResponse>();
            if (feedOp is not null)
            {
                ops.Add(new OperatorBriefResponse
                {
                    GlobalId = feedOp.GlobalId,
                    Name = feedOp.Name,
                    ShortName = feedOp.ShortName
                });
            }

            rawDetail = new StationDetailResponse
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

        StationDetailResponse? suggestedDetail = null;
        if (candidate.SuggestedCanonicalStationId.HasValue && candidate.SuggestedCanonicalStation is not null)
        {
            var stationId = candidate.SuggestedCanonicalStationId.Value;

            var operators = await db.CanonicalStationOperators
                .Where(cso => cso.CanonicalStationId == stationId)
                .Select(cso => new OperatorBriefResponse
                {
                    GlobalId = cso.Operator.GlobalId,
                    Name = cso.Operator.Name,
                    ShortName = cso.Operator.ShortName
                })
                .ToListAsync(ct);

            var routeEntities = await db.CanonicalRoutes
                .Include(r => r.Operator)
                .Where(r => db.StopTimes.Any(st =>
                    st.CanonicalStationId == stationId
                    && st.Trip.CanonicalRouteId == r.Id))
                .ToListAsync(ct);
            var routes = routeEntities.Select(RouteMapper.ToInfoResponse).ToList();

            suggestedDetail = new StationDetailResponse
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

        return new ReconciliationDetailResponse
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
            RawStopDetail = rawDetail,
            SuggestedStationDetail = suggestedDetail,
            AutoMergeVerdict = verdict
        };
    }
}
