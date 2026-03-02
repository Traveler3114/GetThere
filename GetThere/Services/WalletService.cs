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

    public async Task<WalletDto?> GetWalletAsync(string userId)
    {
        var response = await _httpClient.GetAsync($"wallet/{userId}");

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<WalletDto>();

        return null;
    }

    public async Task<IEnumerable<WalletTransactionDto>?> GetTransactionsAsync(string userId)
    {
        var response = await _httpClient.GetAsync($"wallet/{userId}/transactions");

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<IEnumerable<WalletTransactionDto>>();

        return null;
    }

    //public async Task<OperationResult> TopUpAsync(TopUpDto dto)
    //{
    //    var response = await _httpClient.PostAsJsonAsync("wallet/topup", dto);

    //    if (response.IsSuccessStatusCode)
    //        return OperationResult.Ok("Wallet topped up successfully.");

    //    var error = await response.Content.ReadAsStringAsync();
    //    return OperationResult.Fail(error);
    //}
}