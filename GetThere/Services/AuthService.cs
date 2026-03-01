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

    public async Task<OperationResult> LoginAsync(LoginDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/login", dto);

        if (response.IsSuccessStatusCode)
            return OperationResult.Ok("Login successful");

        var error = await response.Content.ReadAsStringAsync();
        return OperationResult.Fail(error);
    }

    public async Task<OperationResult> RegisterAsync(RegisterDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/register", dto);

        if (response.IsSuccessStatusCode)
            return OperationResult.Ok("User registered successfully");

        var error = await response.Content.ReadAsStringAsync();
        return OperationResult.Fail(error);
    }
}