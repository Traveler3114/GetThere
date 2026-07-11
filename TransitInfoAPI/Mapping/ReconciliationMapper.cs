using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

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

    public static ReconciliationDetailResponse ToDetailResponse(
        ReconciliationCandidate candidate,
        double autoNameThreshold, double autoDistThreshold,
        double manualNameThreshold, double manualDistThreshold,
        string? normalizedRaw, string? normalizedStation,
        string explanation, string verdict,
        StationDetailResponse? rawDetail, StationDetailResponse? suggestedDetail) => new()
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
