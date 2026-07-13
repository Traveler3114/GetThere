using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;
using static GetThereShared.Common.HttpHelper;

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
            var response = await _http.GetAsync("countries");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<CountryResponse>>(JsonOptions);
                return OperationResult<List<CountryResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<CountryResponse>>.Fail(problem ?? "Could not load countries");
        }
        catch
        {
            return OperationResult<List<CountryResponse>>.Fail("Network error loading countries");
        }
    }

}
