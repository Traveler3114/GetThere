using GetThereShared.Dtos;
using System.Net.Http.Json;

namespace GetThere.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;

    // A key name for storing the token — just a string constant so we don't typo it
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

        // If login worked and we got a token, save it securely on the device
        // SecureStorage uses the OS keychain/keystore — safe on Android, iOS, Windows
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

    // Any other service can call this to get the token to attach to requests
    public async Task<string?> GetTokenAsync()
        => await SecureStorage.GetAsync(TokenKey);

    // Call this when the user logs out — deletes the token from the device
    public void Logout()
        => SecureStorage.Remove(TokenKey);
}