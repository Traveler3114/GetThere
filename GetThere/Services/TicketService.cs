using GetThereShared.Models;
using System.Net.Http.Json;

namespace GetThere.Services;

public class TicketService
{
    private readonly HttpClient _httpClient;

    public TicketService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<IEnumerable<TicketDto>>> GetTicketsAsync(string userId)
    {
        var response = await _httpClient.GetAsync($"ticket/{userId}");
        return await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<TicketDto>>>()
            ?? OperationResult<IEnumerable<TicketDto>>.Fail("Unexpected error occurred.");
    }

    public async Task<OperationResult<TicketDto>> PurchaseAsync(TicketDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("ticket/purchase", dto);
        return await response.Content.ReadFromJsonAsync<OperationResult<TicketDto>>()
            ?? OperationResult<TicketDto>.Fail("Unexpected error occurred.");
    }
}