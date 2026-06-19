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
            var response = await _http.GetAsync("wallet");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<WalletResponse>(JsonOptions);
                return OperationResult<WalletResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<WalletResponse>.Fail(problem ?? "Could not load wallet");
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
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<WalletResponse>(JsonOptions);
                return OperationResult<WalletResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<WalletResponse>.Fail(problem ?? "Top-up failed");
        }
        catch (Exception ex)
        {
            return OperationResult<WalletResponse>.Fail(ex.Message);
        }
    }

    private static async Task<string?> TryReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (doc.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch { }
        return null;
    }
}
