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
        var response = await _httpClient.GetAsync("payment/providers");
        return await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<PaymentProviderDto>>>()
            ?? OperationResult<IEnumerable<PaymentProviderDto>>.Fail("Unexpected error occurred.");
    }

    public async Task<OperationResult<WalletDto>> TopUpAsync(TopUpDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("payment/topup", dto);
        return await response.Content.ReadFromJsonAsync<OperationResult<WalletDto>>()
            ?? OperationResult<WalletDto>.Fail("Unexpected error occurred.");
    }
}