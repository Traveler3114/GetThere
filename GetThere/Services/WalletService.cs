using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;
using static GetThereShared.Common.HttpHelper;

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


}
