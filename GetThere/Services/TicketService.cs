using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;
using static GetThereShared.Common.HttpHelper;

namespace GetThere.Services;

public class TicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public TicketService(HttpClient http) { _http = http; }

    public async Task<OperationResult<List<TicketOptionResponse>>> GetTicketOptionsAsync()
    {
        try
        {
            var response = await _http.GetAsync("tickets/options");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<TicketOptionResponse>>(JsonOptions);
                return OperationResult<List<TicketOptionResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<TicketOptionResponse>>.Fail(problem ?? "Could not load ticket options");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TicketService] {ex}");
            return OperationResult<List<TicketOptionResponse>>.Fail("Something went wrong. Check your connection and try again.");
        }
    }

    /// <summary>
    /// Buys a ticket.
    /// <para>
    /// Sends an <c>Idempotency-Key</c>. This matters: the API charges the wallet, and
    /// <see cref="Helpers.AuthenticatedHttpHandler"/> replays the request after a 401 token refresh —
    /// without a key that replay is a second purchase. The server returns the original ticket when it
    /// sees a key it has already settled.
    /// </para>
    /// <param name="idempotencyKey">
    /// Pass the same value when retrying the *same* user action; pass null (a fresh key is generated)
    /// for a new purchase.
    /// </param>
    /// </summary>
    public async Task<OperationResult<TicketResponse>> PurchaseTicketAsync(
        PurchaseTicketRequest request, string? idempotencyKey = null)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "tickets/purchase")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            message.Headers.Add("Idempotency-Key", idempotencyKey ?? NewIdempotencyKey());

            var response = await _http.SendAsync(message);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions);
                return OperationResult<TicketResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<TicketResponse>.Fail(problem ?? "Purchase failed");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TicketService] {ex}");
            return OperationResult<TicketResponse>.Fail("Something went wrong. Check your connection and try again.");
        }
    }

    /// <summary>32 hex characters — inside the server's 8..64 range.</summary>
    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    public async Task<OperationResult<List<TicketResponse>>> GetMyTicketsAsync()
    {
        try
        {
            var response = await _http.GetAsync("tickets");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<TicketResponse>>(JsonOptions);
                return OperationResult<List<TicketResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<TicketResponse>>.Fail(problem ?? "Could not load tickets");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TicketService] {ex}");
            return OperationResult<List<TicketResponse>>.Fail("Something went wrong. Check your connection and try again.");
        }
    }

}
