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

    public async Task<UserDto?> LoginAsync(LoginDto dto)
    {

        var response = await _httpClient.PostAsJsonAsync("auth/login", dto);

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<UserDto>();

        return null;
    }

    public async Task<(bool Success, string Message)> RegisterAsync(RegisterDto dto)
    {

        var response = await _httpClient.PostAsJsonAsync("auth/register", dto);

        if (response.IsSuccessStatusCode)
            return (true, "User registered successfully");

        // Try to extract the error message from the response body
        var error = await response.Content.ReadAsStringAsync();
        return (false, error);
    }
}