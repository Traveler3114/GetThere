using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class CountryService
{
    private readonly HttpClient _http;

    public CountryService(HttpClient http) => _http = http;

    public async Task<OperationResult<List<CountryDto>>> GetCountriesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<CountryDto>>>("countries");
            return result ?? OperationResult<List<CountryDto>>.Fail("Could not load countries.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<CountryDto>>.Fail($"Could not load countries: {ex.Message}");
        }
    }
}
