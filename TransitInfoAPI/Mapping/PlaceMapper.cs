using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class PlaceMapper
{
    public static Expression<Func<Place, PlaceResponse>> ToResponseExpression =>
        p => new PlaceResponse
        {
            Id = p.Id,
            Name = p.Name,
            AdmCountryCode = p.AdmCountryCode,
            AdmRegionCode = p.AdmRegionCode,
            Lat = p.Lat,
            Lon = p.Lon,
            Population = p.Population
        };

    public static PlaceResponse ToResponse(Place p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        AdmCountryCode = p.AdmCountryCode,
        AdmRegionCode = p.AdmRegionCode,
        Lat = p.Lat,
        Lon = p.Lon,
        Population = p.Population
    };
}
