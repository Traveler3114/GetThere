using System.Net.Http.Json;

using GetThere.Localization;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class ShopService
{
    private readonly HttpClient _http;

    public ShopService(HttpClient http) => _http = http;

    public async Task<OperationResult<List<TicketableOperatorResponse>>> GetTicketableOperatorsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/ticketable?countryId={countryId.Value}"
                : "operator/ticketable";
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketableOperatorResponse>>>(url);
            return result ?? OperationResult<List<TicketableOperatorResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "ticketable operators"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketableOperatorResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadOperators"] + ex.Message);
        }
    }

    public async Task<OperationResult<List<TicketOptionResponse>>> GetTicketOptionsAsync(int operatorId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TicketOptionResponse>>>(
                $"mock-tickets/{operatorId}/options");
            return result ?? OperationResult<List<TicketOptionResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "ticket options"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<TicketOptionResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadOptions"] + ex.Message);
        }
    }

    public async Task<OperationResult<TicketPurchaseResponse>> PurchaseTicketAsync(
        int operatorId, string optionId, int quantity = 1)
    {
        try
        {
            var body = new PurchaseTicketRequest { OptionId = optionId, Quantity = quantity };
            var response = await _http.PostAsJsonAsync($"mock-tickets/{operatorId}/purchase", body);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return OperationResult<TicketPurchaseResponse>.Fail(LocalizationService.Instance["Shop_LoginRequired"]);

            var result = await response.Content.ReadFromJsonAsync<OperationResult<TicketPurchaseResponse>>();
            return result ?? OperationResult<TicketPurchaseResponse>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "purchase request"));
        }
        catch (Exception ex)
        {
            return OperationResult<TicketPurchaseResponse>.Fail(string.Format(LocalizationService.Instance["Shop_PurchaseFailed"], ex.Message));
        }
    }
}
