using GetThereShared.Dtos;

namespace GetThereAPI.Managers;

public interface IBikeStationCache
{
    List<BikeStationDto> GetAllStations();
    List<BikeStationDto> GetAllStations(string? countryName);
    bool HasStationsInCountry(int providerId, string countryName);
}
