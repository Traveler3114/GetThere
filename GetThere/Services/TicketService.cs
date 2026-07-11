using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class TicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public TicketService(HttpClient http) { _http = http; }

    public async Task<OperationResult<List<TicketOptionResponse>>> GetTicketOptionsAsync()
    {
        try
        {
            var response = await _http.GetAsync("tickets/options");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<TicketOptionResponse>>(JsonOptions);
                return OperationResult<List<TicketOptionResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<TicketOptionResponse>>.Fail(problem ?? "Could not load ticket options");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketOptionResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<TicketResponse>> PurchaseTicketAsync(PurchaseTicketRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("tickets/purchase", request, JsonOptions);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions);
                return OperationResult<TicketResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<TicketResponse>.Fail(problem ?? "Purchase failed");
        }
        catch (Exception ex)
        {
            return OperationResult<TicketResponse>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<TicketResponse>>> GetMyTicketsAsync()
    {
        try
        {
            var response = await _http.GetAsync("tickets");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<TicketResponse>>(JsonOptions);
                return OperationResult<List<TicketResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<TicketResponse>>.Fail(problem ?? "Could not load tickets");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketResponse>>.Fail(ex.Message);
        }
    }

    private static async Task<string?> TryReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (doc.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch { }
        return null;
    }
}
