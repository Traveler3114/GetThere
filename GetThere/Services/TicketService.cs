using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class TicketService
{
    private readonly HttpClient _httpClient;

    public TicketService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // No userId in the URL — the API reads it from the JWT token
    public async Task<OperationResult<IEnumerable<TicketDto>>> GetTicketsAsync()
    {
        var response = await _httpClient.GetAsync("ticket");
        return await response.Content.ReadFromJsonAsync<OperationResult<IEnumerable<TicketDto>>>()
            ?? OperationResult<IEnumerable<TicketDto>>.Fail("Unexpected error occurred.");
    }
}