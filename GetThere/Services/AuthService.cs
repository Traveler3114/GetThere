using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class AuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    public const string TokenKey = "auth_token";
    public const string RefreshTokenKey = "refresh_token";

    public AuthService(HttpClient http) { _http = http; }

    public async Task<OperationResult> RegisterAsync(RegisterRequest request)
    {
        var response = await _http.PostAsJsonAsync("auth/register", request, JsonOptions);
        var result = await response.Content.ReadFromJsonAsync<OperationResult>(JsonOptions);
        return result ?? OperationResult.Fail("Failed to register");
    }

    public async Task<OperationResult<LoginResponse>> LoginAsync(LoginRequest request, bool rememberMe)
    {
        var url = rememberMe ? "auth/login?rememberMe=true" : "auth/login";
        var response = await _http.PostAsJsonAsync(url, request, JsonOptions);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<LoginResponse>>(JsonOptions);

        if (result?.Success == true && result.Data is not null)
        {
            await SecureStorage.Default.SetAsync(TokenKey, result.Data.AccessToken);
            await SecureStorage.Default.SetAsync(RefreshTokenKey, result.Data.RefreshToken);
            await StoreRememberMeAsync(rememberMe);
        }

        return result ?? OperationResult<LoginResponse>.Fail("Login failed");
    }

    public async Task<bool> TryRefreshTokenAsync()
    {
        var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        if (string.IsNullOrWhiteSpace(refreshToken)) return false;

        var response = await _http.PostAsJsonAsync("auth/refresh", new RefreshTokenRequest(refreshToken), JsonOptions);
        var result = await response.Content.ReadFromJsonAsync<OperationResult<RefreshTokenResponse>>(JsonOptions);

        if (result?.Success == true && result.Data is not null)
        {
            await SecureStorage.Default.SetAsync(TokenKey, result.Data.AccessToken);
            await SecureStorage.Default.SetAsync(RefreshTokenKey, result.Data.RefreshToken);
            return true;
        }

        return false;
    }

    public async Task Logout()
    {
        var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            try
            {
                await _http.PostAsJsonAsync("auth/logout", new RefreshTokenRequest(refreshToken), JsonOptions);
            }
            catch { }
        }

        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
        await StoreRememberMeAsync(false);
    }

    public async Task<string?> GetTokenAsync() => await SecureStorage.Default.GetAsync(TokenKey);

    public async Task<string?> GetFullNameAsync()
    {
        try
        {
            var token = await GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return null;

            var handler = new JwtPayloadReader(token);
            return handler.GetGivenName();
        }
        catch { return null; }
    }

    public async Task<string?> GetEmailAsync()
    {
        try
        {
            var token = await GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return null;

            var handler = new JwtPayloadReader(token);
            return handler.GetEmail();
        }
        catch { return null; }
    }

    public bool IsLoggedIn => SecureStorage.Default.GetAsync(TokenKey).Result is not null;

    public bool GetRememberMe() => Preferences.Default.Get("remember_me", false);

    private async Task StoreRememberMeAsync(bool rememberMe)
    {
        if (rememberMe)
            Preferences.Default.Set("remember_me", true);
        else
            Preferences.Default.Remove("remember_me");
    }

    private class JwtPayloadReader(string token)
    {
        private readonly JsonElement _payload = ParsePayload(token);

        private static JsonElement ParsePayload(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return default;

            var json = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(PadBase64(parts[1])));

            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        private static string PadBase64(string base64)
        {
            base64 = base64.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return base64;
        }

        public string? GetGivenName() =>
            _payload.TryGetProperty("given_name", out var name) ? name.GetString() : null;

        public string? GetEmail() =>
            _payload.TryGetProperty("email", out var email) ? email.GetString() : null;
    }
}
