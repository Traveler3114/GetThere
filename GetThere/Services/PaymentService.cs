using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class PaymentService
{
    private readonly HttpClient _httpClient;

    public PaymentService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<IEnumerable<PaymentProviderDto>>> GetProvidersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("payment/providers");
            var result = await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<PaymentProviderDto>>>();

            if (result != null)
                return result;

            return OperationResult<IEnumerable<PaymentProviderDto>>.Fail(response.IsSuccessStatusCode
                ? "No payment providers were returned."
                : $"Could not load payment providers ({(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return OperationResult<IEnumerable<PaymentProviderDto>>.Fail($"Could not load payment providers: {ex.Message}");
        }
    }

    public async Task<OperationResult<WalletDto>> TopUpAsync(TopUpDto dto)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("payment/topup", dto);
            var result = await response.Content.ReadFromJsonAsync<OperationResult<WalletDto>>();

            if (result != null)
                return result;

            return OperationResult<WalletDto>.Fail(response.IsSuccessStatusCode
                ? "Top up completed but no wallet payload was returned."
                : $"Top up failed ({(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return OperationResult<WalletDto>.Fail($"Top up failed: {ex.Message}");
        }
    }
}
