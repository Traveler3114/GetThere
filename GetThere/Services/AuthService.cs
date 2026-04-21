using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GetThere.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private const string TokenKey = "jwt_token";
    private const string RefreshTokenKey = "refresh_token";
    private const string RememberMeKey = "remember_me";

    public AuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperationResult<UserDto>> LoginAsync(LoginDto dto, bool rememberMe = false)
    {
        var response = await _httpClient.PostAsJsonAsync($"auth/login?rememberMe={rememberMe.ToString().ToLowerInvariant()}", dto);
        if (!response.IsSuccessStatusCode)
            return OperationResult<UserDto>.Fail("Invalid credentials");

        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        if (result == null || string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.RefreshToken))
            return OperationResult<UserDto>.Fail("Unexpected error occurred.");

        await SecureStorage.SetAsync(TokenKey, result.AccessToken);
        await SecureStorage.SetAsync(RefreshTokenKey, result.RefreshToken);
        Preferences.Default.Set(RememberMeKey, rememberMe);

        return OperationResult<UserDto>.Ok(
            new UserDto
            {
                Id = result.User.Id,
                Email = result.User.Email,
                FullName = result.User.FullName,
                Token = result.AccessToken
            },
            "Login successful");
    }

    public async Task<OperationResult> RegisterAsync(RegisterDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/register", dto);
        return await response.Content.ReadFromJsonAsync<OperationResult>()
            ?? OperationResult.Fail("Unexpected error occurred.");
    }

    public async Task<string?> GetTokenAsync()
        => await SecureStorage.GetAsync(TokenKey);

    public async Task<bool> TryRefreshTokenAsync()
    {
        try
        {
            var currentRefreshToken = await SecureStorage.GetAsync(RefreshTokenKey);
            if (string.IsNullOrWhiteSpace(currentRefreshToken))
                return false;

            var response = await _httpClient.PostAsJsonAsync("auth/refresh", new RefreshTokenRequestDto
            {
                RefreshToken = currentRefreshToken
            });

            if (!response.IsSuccessStatusCode)
                return false;

            var refreshResponse = await response.Content.ReadFromJsonAsync<RefreshTokenResponseDto>();
            if (refreshResponse == null
                || string.IsNullOrWhiteSpace(refreshResponse.AccessToken)
                || string.IsNullOrWhiteSpace(refreshResponse.RefreshToken))
            {
                return false;
            }

            await SecureStorage.SetAsync(TokenKey, refreshResponse.AccessToken);
            await SecureStorage.SetAsync(RefreshTokenKey, refreshResponse.RefreshToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool GetRememberMe()
        => Preferences.Default.Get(RememberMeKey, false);

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
        string? refreshToken = null;
        string? accessToken = null;
        try
        {
            refreshToken = SecureStorage.GetAsync(RefreshTokenKey).GetAwaiter().GetResult();
            accessToken = SecureStorage.GetAsync(TokenKey).GetAwaiter().GetResult();
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(accessToken))
        {
            var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "auth/logout")
            {
                Content = JsonContent.Create(new RefreshTokenRequestDto
                {
                    RefreshToken = refreshToken
                })
            };

            logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _ = _httpClient.SendAsync(logoutRequest);
        }

        SecureStorage.Remove(TokenKey);
        SecureStorage.Remove(RefreshTokenKey);
        Preferences.Default.Remove(RememberMeKey);
    }
}
