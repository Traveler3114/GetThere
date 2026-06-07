using System.Net.Http.Json;

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
                ? "No payment providers were returned."
                : $"Could not load payment providers ({(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return OperationResult<IEnumerable<PaymentProviderResponse>>.Fail($"Could not load payment providers: {ex.Message}");
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
                ? "Top up completed but no wallet payload was returned."
                : $"Top up failed ({(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return OperationResult<WalletResponse>.Fail($"Top up failed: {ex.Message}");
        }
    }
}
