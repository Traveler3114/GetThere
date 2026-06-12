using System.Net.Http.Json;

using GetThere.Localization;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class PaymentService
{
    private readonly HttpClient _httpClient;

    public PaymentService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<IEnumerable<PaymentProviderResponse>>> GetProvidersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("payment/providers");
            var result = await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<PaymentProviderResponse>>>();

            if (result is not null)
                return result;

            return OperationResult<IEnumerable<PaymentProviderResponse>>.Fail(response.IsSuccessStatusCode
                ? LocalizationService.Instance["Payment_NoProvidersReturned"]
                : string.Format(LocalizationService.Instance["Payment_LoadFailed"], $"({(int)response.StatusCode})"));
        }
        catch (Exception ex)
        {
            return OperationResult<IEnumerable<PaymentProviderResponse>>.Fail(string.Format(LocalizationService.Instance["Payment_LoadFailed"], ex.Message));
        }
    }

    public async Task<OperationResult<WalletResponse>> TopUpAsync(TopUpRequest dto)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("payment/topup", dto);
            var result = await response.Content.ReadFromJsonAsync<OperationResult<WalletResponse>>();

            if (result is not null)
                return result;

            return OperationResult<WalletResponse>.Fail(response.IsSuccessStatusCode
                ? LocalizationService.Instance["Payment_TopUpNoPayload"]
                : string.Format(LocalizationService.Instance["Payment_TopUpFailed"], $"({(int)response.StatusCode})"));
        }
        catch (Exception ex)
        {
            return OperationResult<WalletResponse>.Fail(string.Format(LocalizationService.Instance["Payment_TopUpFailed"], ex.Message));
        }
    }
}
