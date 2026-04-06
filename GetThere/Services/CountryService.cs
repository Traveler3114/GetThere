using System.Diagnostics;
using System.Net.Http.Json;
using GetThereShared.Common;
using GetThereShared.Dtos;

namespace GetThere.Services;

/// <summary>
/// Fetches the list of available countries from the API.
/// Used to populate the country selector in Settings.
/// </summary>
public class CountryService
{
    private readonly HttpClient _http;

    public CountryService(HttpClient http) => _http = http;

    /// <summary>Returns all available countries from GET /countries.</summary>
    public async Task<List<CountryDto>?> GetCountriesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<CountryDto>>>("countries");
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[CountryService] GetCountries failed: {ex.Message}");
            return null;
        }
    }
}
