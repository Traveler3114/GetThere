using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class MobilityStationMapper
{
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
