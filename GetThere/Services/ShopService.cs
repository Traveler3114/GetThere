using System.Diagnostics;
using System.Net.Http.Json;
using GetThereShared.Common;
using GetThereShared.Dtos;

namespace GetThere.Services;

/// <summary>
/// Provides access to the shop-related API endpoints:
/// GET /operator/ticketable, GET /mock-tickets/{id}/options, POST /mock-tickets/{id}/purchase.
/// </summary>
public class ShopService
{
    private readonly HttpClient _http;

    public ShopService(HttpClient http) => _http = http;

    /// <summary>Returns operators available for ticket purchase, optionally filtered by country.</summary>
    public async Task<List<TicketableOperatorDto>?> GetTicketableOperatorsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/ticketable?countryId={countryId.Value}"
                : "operator/ticketable";
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketableOperatorDto>>>(url);
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShopService] GetTicketableOperators failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns available ticket options for a specific operator.</summary>
    public async Task<List<MockTicketOptionDto>?> GetTicketOptionsAsync(int operatorId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<MockTicketOptionDto>>>(
                $"mock-tickets/{operatorId}/options");
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShopService] GetTicketOptions({operatorId}) failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Purchases a mock ticket for the specified operator and option.</summary>
    public async Task<OperationResult<MockTicketResultDto>?> PurchaseTicketAsync(
        int operatorId, string optionId, int quantity = 1)
    {
        try
        {
            var body = new MockTicketPurchaseRequest { OptionId = optionId, Quantity = quantity };
            var response = await _http.PostAsJsonAsync($"mock-tickets/{operatorId}/purchase", body);
            return await response.Content.ReadFromJsonAsync<OperationResult<MockTicketResultDto>>();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShopService] PurchaseTicket({operatorId},{optionId}) failed: {ex.Message}");
            return null;
        }
    }
}
