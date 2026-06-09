using System.Collections.Concurrent;

using GetThereShared.Contracts;

namespace TransitInfoAPI.Core;

public sealed class BikeStationCache
{
    private readonly ConcurrentDictionary<int, List<BikeStationResponse>> _stations = new();

    public List<BikeStationResponse> GetAllStations()
        => _stations.Values.SelectMany(s => s).ToList();

    public List<BikeStationResponse> GetAllStations(string? countryName)
    {
        var all = _stations.Values.SelectMany(s => s);
        if (string.IsNullOrEmpty(countryName))
            return all.ToList();
        return all
            .Where(s => s.CountryName.Equals(countryName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public List<BikeStationResponse> GetStations(int providerId)
        => _stations.TryGetValue(providerId, out var list) ? list : [];

    public bool HasStationsInCountry(int providerId, string countryName)
    {
        if (!_stations.TryGetValue(providerId, out var list))
            return false;
        return list.Any(s => s.CountryName.Equals(countryName, StringComparison.OrdinalIgnoreCase));
    }

    public void Update(int providerId, List<BikeStationResponse> stations)
    {
        _stations[providerId] = stations;
    }
}
