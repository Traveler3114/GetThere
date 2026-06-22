using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class StationMapper
{
    public static Expression<Func<CanonicalStation, StationResponse>> ToResponseExpression =>
        s => new StationResponse
        {
            Id = s.Id,
            GlobalId = s.GlobalId,
            OnestopId = s.OnestopId,
            Name = s.Name,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            StationType = s.StationType.ToString(),
            PrimaryRouteType = s.PrimaryRouteType.ToString(),
            CountryName = s.Country != null ? s.Country.Name : null,
            CityName = s.City != null ? s.City.Name : null
        };

    public static StationResponse ToResponse(CanonicalStation s) => new()
    {
        Id = s.Id,
        GlobalId = s.GlobalId,
        OnestopId = s.OnestopId,
        Name = s.Name,
        Latitude = s.Latitude,
        Longitude = s.Longitude,
        StationType = s.StationType.ToString(),
        PrimaryRouteType = s.PrimaryRouteType.ToString(),
        CountryName = s.Country?.Name,
        CityName = s.City?.Name
    };

    public static StationOperatorResponse ToOperatorResponse(CanonicalStationOperator cso) => new()
    {
        GlobalId = cso.Operator.GlobalId,
        Name = cso.Operator.Name
    };
}
