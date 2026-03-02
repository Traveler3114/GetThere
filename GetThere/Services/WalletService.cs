using GetThereShared.Models;
using System.Net.Http.Json;

namespace GetThere.Services;

public class WalletService
{
    private readonly HttpClient _httpClient;

    public WalletService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<WalletDto>> GetWalletAsync(string userId)
    {
        var response = await _httpClient.GetAsync($"wallet/{userId}");
        return await response.Content.ReadFromJsonAsync<OperationResult<WalletDto>>()
            ?? OperationResult<WalletDto>.Fail("Unexpected error occurred.");
    }

    public async Task<OperationResult<IEnumerable<WalletTransactionDto>>> GetTransactionsAsync(string userId)
    {
        var response = await _httpClient.GetAsync($"wallet/{userId}/transactions");
        return await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<WalletTransactionDto>>>()
            ?? OperationResult<IEnumerable<WalletTransactionDto>>.Fail("Unexpected error occurred.");
    }

    //public async Task<OperationResult<WalletDto>> TopUpAsync(TopUpDto dto)
    //{
    //    var response = await _httpClient.PostAsJsonAsync("wallet/topup", dto);
    //    return await response.Content.ReadFromJsonAsync<OperationResult<WalletDto>>()
    //        ?? OperationResult<WalletDto>.Fail("Unexpected error occurred.");
    //}
}