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

    public async Task<UserDto?> LoginAsync(string email, string password)
    {
        var request = new LoginRequest
        {
            Email = email,
            Password = password
        };

        var response = await _httpClient.PostAsJsonAsync("auth/login", request);

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<UserDto>();

        return null;
    }

    public async Task<(bool Success, string Message)> RegisterAsync(
        string username, string email, string password, string? fullName = null, string? city = null)
    {
        var request = new RegisterRequest
        {
            Username = username,
            Email = email,
            Password = password,
            FullName = fullName,
            City = city
        };

        var response = await _httpClient.PostAsJsonAsync("auth/register", request);

        if (response.IsSuccessStatusCode)
            return (true, "User registered successfully");

        // Try to extract the error message from the response body
        var error = await response.Content.ReadAsStringAsync();
        return (false, error);
    }
}