using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public interface IBikeStationCache
{
    List<BikeStationResponse> GetAllStations();
    List<BikeStationResponse> GetAllStations(string? countryName);
    bool HasStationsInCountry(int providerId, string countryName);
}
