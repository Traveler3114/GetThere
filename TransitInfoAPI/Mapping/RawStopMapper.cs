using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class RawStopMapper
{
    public static Expression<Func<RawStop, RawStopResponse>> ToResponseExpression =>
        rs => new RawStopResponse
        {
            Id = rs.Id,
            RawStopId = rs.RawStopId,
            Name = rs.Name,
            Lat = rs.Lat,
            Lon = rs.Lon,
            StationType = rs.StationType.ToString(),
            RouteType = rs.RouteType != null ? rs.RouteType.Value.ToString() : string.Empty,
            CanonicalStationId = rs.CanonicalStationId,
            ReconciliationStatus = rs.ReconciliationStatus.ToString()
        };

    public static RawStopResponse ToResponse(RawStop rs) => new()
    {
        Id = rs.Id,
        RawStopId = rs.RawStopId,
        Name = rs.Name,
        Lat = rs.Lat,
        Lon = rs.Lon,
        StationType = rs.StationType.ToString(),
        RouteType = rs.RouteType?.ToString() ?? string.Empty,
        CanonicalStationId = rs.CanonicalStationId,
        ReconciliationStatus = rs.ReconciliationStatus.ToString()
    };
}
