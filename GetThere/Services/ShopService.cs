using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class ShopService
{
    private readonly HttpClient _http;

    public ShopService(HttpClient http) => _http = http;

    public async Task<OperationResult<List<TicketableOperatorDto>>> GetTicketableOperatorsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/ticketable?countryId={countryId.Value}"
                : "operator/ticketable";
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketableOperatorDto>>>(url);
            return result ?? OperationResult<List<TicketableOperatorDto>>.Fail("Could not load ticketable operators.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketableOperatorDto>>.Fail($"Could not load ticketable operators: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<MockTicketOptionDto>>> GetTicketOptionsAsync(int operatorId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<MockTicketOptionDto>>>(
                $"mock-tickets/{operatorId}/options");
            return result ?? OperationResult<List<MockTicketOptionDto>>.Fail("Could not load ticket options.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MockTicketOptionDto>>.Fail($"Could not load ticket options: {ex.Message}");
        }
    }

    public async Task<OperationResult<MockTicketResultDto>> PurchaseTicketAsync(
        int operatorId, string optionId, int quantity = 1)
    {
        try
        {
            var body = new MockTicketPurchaseRequest { OptionId = optionId, Quantity = quantity };
            var response = await _http.PostAsJsonAsync($"mock-tickets/{operatorId}/purchase", body);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return OperationResult<MockTicketResultDto>.Fail("Please log in to purchase tickets.");

            var result = await response.Content.ReadFromJsonAsync<OperationResult<MockTicketResultDto>>();
            return result ?? OperationResult<MockTicketResultDto>.Fail("Purchase failed.");
        }
        catch (Exception ex)
        {
            return OperationResult<MockTicketResultDto>.Fail($"Purchase failed: {ex.Message}");
        }
    }
}
