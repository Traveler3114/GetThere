using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class FeedVersionMapper
{
    public static Expression<Func<FeedVersion, FeedVersionResponse>> ToResponseExpression =>
        fv => new FeedVersionResponse
        {
            Id = fv.Id,
            FeedId = fv.FeedId,
            Sha1 = fv.Sha1,
            FetchedAt = fv.FetchedAt,
            ImportedAt = fv.ImportedAt,
            IsActive = fv.IsActive,
            ImportStatus = fv.ImportStatus.ToString(),
            ImportError = fv.ImportError,
            ServiceLevelStart = fv.ServiceLevelStart,
            ServiceLevelEnd = fv.ServiceLevelEnd,
            StopCount = fv.StopCount,
            RouteCount = fv.RouteCount,
            TripCount = fv.TripCount
        };

    public static FeedVersionResponse ToResponse(FeedVersion fv) => new()
    {
        Id = fv.Id,
        FeedId = fv.FeedId,
        Sha1 = fv.Sha1,
        FetchedAt = fv.FetchedAt,
        ImportedAt = fv.ImportedAt,
        IsActive = fv.IsActive,
        ImportStatus = fv.ImportStatus.ToString(),
        ImportError = fv.ImportError,
        ServiceLevelStart = fv.ServiceLevelStart,
        ServiceLevelEnd = fv.ServiceLevelEnd,
        StopCount = fv.StopCount,
        RouteCount = fv.RouteCount,
        TripCount = fv.TripCount
    };
}
