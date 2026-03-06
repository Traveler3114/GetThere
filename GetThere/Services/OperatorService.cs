using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class OperatorService
{
    private readonly HttpClient _http;

    public OperatorService(HttpClient http) => _http = http;

    public async Task<List<TransitOperatorDto>> GetAllAsync()
    {
        var result = await _http
            .GetFromJsonAsync<OperationResult<IEnumerable<TransitOperatorDto>>>(
                "operator");
        return result?.Data?.ToList() ?? [];
    }
}