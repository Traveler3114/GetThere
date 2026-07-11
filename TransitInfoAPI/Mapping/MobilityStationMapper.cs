using System.Linq.Expressions;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class MobilityStationMapper
{
    public static Expression<Func<MobilityStation, MobilityStationResponse>> ToResponseExpression =>
        ms => new MobilityStationResponse
        {
            Id = ms.Id,
            StationId = ms.StationId,
            Name = ms.Name,
            Latitude = ms.Latitude,
            Longitude = ms.Longitude,
            AvailableVehicles = ms.AvailableVehicles,
            Capacity = ms.Capacity,
            ProviderName = ms.Operator != null ? ms.Operator.Name : string.Empty,
            LastUpdated = ms.LastUpdated,
            CountryName = ms.Country != null ? ms.Country.Name : null,
            CountryCode = ms.Country != null ? ms.Country.IsoCode : null
        };

    public static MobilityStationResponse ToResponse(MobilityStation ms) => new()
    {
        Id = ms.Id,
        StationId = ms.StationId,
        Name = ms.Name,
        Latitude = ms.Latitude,
        Longitude = ms.Longitude,
        AvailableVehicles = ms.AvailableVehicles,
        Capacity = ms.Capacity,
        ProviderName = ms.Operator?.Name ?? string.Empty,
        LastUpdated = ms.LastUpdated,
        CountryName = ms.Country?.Name,
        CountryCode = ms.Country?.IsoCode
    };
}
