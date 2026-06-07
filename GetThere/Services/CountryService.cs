using System.Net.Http.Json;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class CountryService
{
    private readonly HttpClient _http;

    public CountryService(HttpClient http) => _http = http;

    public async Task<OperationResult<List<CountryResponse>>> GetCountriesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<CountryResponse>>>("countries");
            return result ?? OperationResult<List<CountryResponse>>.Fail("No response received from API when loading countries.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<CountryResponse>>.Fail($"Could not load countries: {ex.Message}");
        }
    }
}
