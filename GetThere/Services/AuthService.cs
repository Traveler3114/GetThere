using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GetThere.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private const string TokenKey = "jwt_token";
    private const string TokenFallbackKey = "jwt_token_fallback";

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
            await SaveTokenAsync(result.Data.Token);

        return result;
    }

    public async Task<OperationResult> RegisterAsync(RegisterDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/register", dto);
        return await response.Content.ReadFromJsonAsync<OperationResult>()
            ?? OperationResult.Fail("Unexpected error occurred.");
    }

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            var secureToken = await SecureStorage.GetAsync(TokenKey);
            if (!string.IsNullOrWhiteSpace(secureToken))
                return secureToken;
        }
        catch
        {
            // Some Android devices can fail to create/read keystore-backed entries.
            // Fall back to Preferences so login remains usable in development.
        }

        var fallbackToken = Preferences.Default.Get(TokenFallbackKey, string.Empty);
        return string.IsNullOrWhiteSpace(fallbackToken) ? null : fallbackToken;
    }

    /// <summary>
    /// Decodes the JWT payload and returns the claims as a dictionary.
    /// No signature verification — safe to use client-side for display purposes only.
    /// </summary>
    public async Task<Dictionary<string, JsonElement>?> GetTokenClaimsAsync()
    {
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            // JWT = header.payload.signature — we only need the payload
            var payload = token.Split('.')[1];

            // Base64url → Base64 (fix padding and character replacements)
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convenience helpers so callers don't need to know claim names.
    /// </summary>
    public async Task<string?> GetFullNameAsync()
    {
        var claims = await GetTokenClaimsAsync();
        return claims?.GetValueOrDefault("given_name").GetString();
    }

    public async Task<string?> GetEmailAsync()
    {
        var claims = await GetTokenClaimsAsync();
        return claims?.GetValueOrDefault("email").GetString();
    }

    public void Logout()
    {
        try
        {
            SecureStorage.Remove(TokenKey);
        }
        catch
        {
        }

        Preferences.Default.Remove(TokenFallbackKey);
    }

    private static async Task SaveTokenAsync(string token)
    {
        try
        {
            await SecureStorage.SetAsync(TokenKey, token);
            Preferences.Default.Remove(TokenFallbackKey);
        }
        catch
        {
            Preferences.Default.Set(TokenFallbackKey, token);
        }
    }
}
