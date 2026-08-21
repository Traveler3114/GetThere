using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class AlertMapper
{
    public static AlertResponse ToResponse(Alert a) => new()
    {
        Id = a.Id,
        FeedId = a.FeedId,
        OperatorId = a.OperatorId,
        HeaderText = a.HeaderText,
        DescriptionText = a.DescriptionText,
        Url = a.Url,
        Cause = a.Cause,
        Effect = a.Effect,
        ActivePeriodStart = a.ActivePeriodStart,
        ActivePeriodEnd = a.ActivePeriodEnd,
        FetchedAt = a.FetchedAt,
        AffectedStopIds = a.AffectedStopIds,
        AffectedRouteIds = a.AffectedRouteIds,
        AffectedTripIds = a.AffectedTripIds,
        AffectedAgencyIds = a.AffectedAgencyIds,
        Kind = a.Kind,
        SourceKey = a.SourceKey,
        SourceUrl = a.SourceUrl,
        Latitude = a.Latitude,
        Longitude = a.Longitude,
        GeometryGeoJson = a.GeometryGeoJson,
        Severity = a.Severity,
        MatchedRouteIds = a.MatchedRouteIds
    };
}
