using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private const string TokenKey = "jwt_token";

    public AuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<UserDto>> LoginAsync(LoginDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/login", dto);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<UserDto>>()
            ?? OperationResult<UserDto>.Fail("Unexpected error occurred.");

        if (result.Success && result.Data?.Token != null)
            await SecureStorage.SetAsync(TokenKey, result.Data.Token);

        return result;
    }

    public async Task<OperationResult> RegisterAsync(RegisterDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/register", dto);
        return await response.Content.ReadFromJsonAsync<OperationResult>()
            ?? OperationResult.Fail("Unexpected error occurred.");
    }

    public async Task<string?> GetTokenAsync()
        => await SecureStorage.GetAsync(TokenKey);

    public void Logout()
        => SecureStorage.Remove(TokenKey);
}