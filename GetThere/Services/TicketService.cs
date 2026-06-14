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
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketOptionResponse>>>("tickets/options", JsonOptions);
            return result ?? OperationResult<List<TicketOptionResponse>>.Fail("Could not load ticket options");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketOptionResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<TicketResponse>> PurchaseTicketAsync(TicketPurchaseRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("tickets/purchase", request, JsonOptions);
            var result = await response.Content.ReadFromJsonAsync<OperationResult<TicketResponse>>(JsonOptions);
            return result ?? OperationResult<TicketResponse>.Fail("Purchase failed");
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
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketResponse>>>("tickets", JsonOptions);
            return result ?? OperationResult<List<TicketResponse>>.Fail("Could not load tickets");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketResponse>>.Fail(ex.Message);
        }
    }
}
