using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class WalletService
{
    private readonly HttpClient _httpClient;

    public WalletService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<WalletDto>> GetWalletAsync()
    {
        var response = await _httpClient.GetAsync("wallet");
        return await response.Content.ReadFromJsonAsync<OperationResult<WalletDto>>()
            ?? OperationResult<WalletDto>.Fail("Unexpected error occurred.");
    }

    public async Task<OperationResult<IEnumerable<WalletTransactionDto>>> GetTransactionsAsync()
    {
        var response = await _httpClient.GetAsync("wallet/transactions");
        return await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<WalletTransactionDto>>>()
            ?? OperationResult<IEnumerable<WalletTransactionDto>>.Fail("Unexpected error occurred.");
    }
}