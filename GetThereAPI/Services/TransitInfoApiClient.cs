using System.Net.Http.Json;

using GetThereShared.Contracts;

namespace GetThereAPI.Services;

public sealed class TransitInfoApiClient
{
    private readonly HttpClient _http;

    public TransitInfoApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<BikeStationResponse>> GetAllStationsAsync(string? countryName = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(countryName)
            ? "/bike-stations"
            : $"/bike-stations?countryName={Uri.EscapeDataString(countryName)}";

        try
        {
            var result = await _http.GetFromJsonAsync<List<BikeStationResponse>>(url, ct);
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> HasStationsInCountryAsync(int providerId, string countryName, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<bool>(
                $"/bike-stations/{providerId}/exists?countryName={Uri.EscapeDataString(countryName)}", ct);
            return result;
        }
        catch
        {
            return false;
        }
    }
}
