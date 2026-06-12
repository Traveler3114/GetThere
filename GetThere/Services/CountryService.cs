using System.Net.Http.Json;

using GetThere.Localization;
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
            return result ?? OperationResult<List<CountryResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "countries"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<CountryResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadCountries"] + ex.Message);
        }
    }
}
