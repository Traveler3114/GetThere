using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class WalletService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public WalletService(HttpClient http) { _http = http; }

    public async Task<OperationResult<WalletResponse>> GetWalletAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<WalletResponse>>("wallet", JsonOptions);
            return result ?? OperationResult<WalletResponse>.Fail("Could not load wallet");
        }
        catch (Exception ex)
        {
            return OperationResult<WalletResponse>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<WalletResponse>> TopUpAsync(decimal amount, string paymentMethod = "card")
    {
        try
        {
            var request = new TopUpRequest { Amount = amount, PaymentMethod = paymentMethod };
            var response = await _http.PostAsJsonAsync("wallet/topup", request, JsonOptions);
            var result = await response.Content.ReadFromJsonAsync<OperationResult<WalletResponse>>(JsonOptions);
            return result ?? OperationResult<WalletResponse>.Fail("Top-up failed");
        }
        catch (Exception ex)
        {
            return OperationResult<WalletResponse>.Fail(ex.Message);
        }
    }
}
