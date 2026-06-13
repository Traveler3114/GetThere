using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class CountryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public CountryService(HttpClient http) { _http = http; }

    public async Task<OperationResult<List<CountryResponse>>> GetCountriesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<CountryResponse>>>("countries", JsonOptions);
            return result ?? OperationResult<List<CountryResponse>>.Fail("Could not load countries");
        }
        catch
        {
            return OperationResult<List<CountryResponse>>.Fail("Network error loading countries");
        }
    }
}
