using GetThereAPI.Models;
using GetThereShared.Models;
using System.Net.Http.Json;

namespace GetThere.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;

    public AuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<UserDto>> LoginAsync(LoginDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/login", dto);
        return await response.Content.ReadFromJsonAsync<OperationResult<UserDto>>()
            ?? OperationResult<UserDto>.Fail("Unexpected error occurred.");
    }

    public async Task<OperationResult> RegisterAsync(RegisterDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/register", dto);
        return await response.Content.ReadFromJsonAsync<OperationResult>()
            ?? OperationResult.Fail("Unexpected error occurred.");
    }
}