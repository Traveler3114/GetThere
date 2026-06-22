using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class TripMapper
{
    public static TripResponse ToResponse(Trip t) => new()
    {
        Id = t.Id,
        TripId = t.TripId,
        Headsign = t.TripHeadsign,
        ShortName = t.TripShortName,
        DirectionId = t.DirectionId,
        RouteName = t.CanonicalRoute != null ? t.CanonicalRoute.LongName : null,
        RouteType = t.CanonicalRoute != null ? t.CanonicalRoute.RouteType.ToString() : null,
        ActiveToday = false
    };
}
