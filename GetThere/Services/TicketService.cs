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

    public async Task<OperationResult<List<TicketTypeResponse>>> GetTicketTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketTypeResponse>>>("tickets/types", JsonOptions);
            return result ?? OperationResult<List<TicketTypeResponse>>.Fail("Could not load ticket types");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketTypeResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<TicketInstanceResponse>> PurchaseTicketAsync(PurchaseTicketRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("tickets/purchase", request, JsonOptions);
            var result = await response.Content.ReadFromJsonAsync<OperationResult<TicketInstanceResponse>>(JsonOptions);
            return result ?? OperationResult<TicketInstanceResponse>.Fail("Purchase failed");
        }
        catch (Exception ex)
        {
            return OperationResult<TicketInstanceResponse>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<TicketInstanceResponse>>> GetMyTicketsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketInstanceResponse>>>("tickets", JsonOptions);
            return result ?? OperationResult<List<TicketInstanceResponse>>.Fail("Could not load tickets");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketInstanceResponse>>.Fail(ex.Message);
        }
    }
}
