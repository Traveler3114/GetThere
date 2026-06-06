using GetThereShared.Common;
using GetThereShared.Contracts;
using System.Net.Http.Json;

namespace GetThere.Services;

public class WalletService
{
    private readonly HttpClient _httpClient;

    public WalletService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<WalletResponse>> GetWalletAsync()
    {
        var response = await _httpClient.GetAsync("wallet");
        return await response.Content.ReadFromJsonAsync<OperationResult<WalletResponse>>()
            ?? OperationResult<WalletResponse>.Fail("Unexpected error occurred.");
    }

    public async Task<OperationResult<IEnumerable<WalletTransactionResponse>>> GetTransactionsAsync()
    {
        var response = await _httpClient.GetAsync("wallet/transactions");
        return await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<WalletTransactionResponse>>>()
            ?? OperationResult<IEnumerable<WalletTransactionResponse>>.Fail("Unexpected error occurred.");
    }
}