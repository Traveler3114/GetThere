using System.Diagnostics;
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
    private string? _cachedToken;
    private string? _cachedRefreshToken;
    public const string TokenKey = "auth_token";
    public const string RefreshTokenKey = "refresh_token";

    public AuthService(HttpClient http) { _http = http; }

    public async Task<OperationResult> RegisterAsync(RegisterRequest request)
    {
        var response = await _http.PostAsJsonAsync("auth/register", request, JsonOptions);
        if (response.IsSuccessStatusCode)
            return OperationResult.Ok("USER_REGISTERED");

        var problem = await TryReadProblemAsync(response);
        return OperationResult.Fail(problem ?? "Registration failed");
    }

    public async Task<OperationResult<LoginResponse>> LoginAsync(LoginRequest request, bool rememberMe)
    {
        var url = rememberMe ? "auth/login?rememberMe=true" : "auth/login";
        var response = await _http.PostAsJsonAsync(url, request, JsonOptions);

        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (data is not null)
            {
                _cachedToken = data.AccessToken;
                _cachedRefreshToken = data.RefreshToken;
                await SecureStorage.Default.SetAsync(TokenKey, data.AccessToken);
                await SecureStorage.Default.SetAsync(RefreshTokenKey, data.RefreshToken);
                await StoreRememberMeAsync(rememberMe);
                return OperationResult<LoginResponse>.Ok(data);
            }
        }

        var problem = await TryReadProblemAsync(response);
        return OperationResult<LoginResponse>.Fail(problem ?? "Login failed");
    }

    public async Task<bool> TryRefreshTokenAsync()
    {
        var refreshToken = await GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken)) return false;

        var response = await _http.PostAsJsonAsync("auth/refresh", new RefreshTokenRequest(refreshToken), JsonOptions);

        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(JsonOptions);
            if (data is not null)
            {
                _cachedToken = data.AccessToken;
                _cachedRefreshToken = data.RefreshToken;
                await SecureStorage.Default.SetAsync(TokenKey, data.AccessToken);
                await SecureStorage.Default.SetAsync(RefreshTokenKey, data.RefreshToken);
                return true;
            }
        }

        return false;
    }

    public async Task Logout()
    {
        var refreshToken = await GetRefreshTokenAsync();
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            try
            {
                await _http.PostAsJsonAsync("auth/logout", new RefreshTokenRequest(refreshToken), JsonOptions);
            }
            catch (Exception ex) { Trace.WriteLine($"[AuthService] Logout server call failed: {ex.Message}"); }
        }

        _cachedToken = null;
        _cachedRefreshToken = null;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
        ClearGuest();
        await StoreRememberMeAsync(false);
    }

    public async Task<string?> GetTokenAsync()
    {
        if (_cachedToken is not null)
            return _cachedToken;
        _cachedToken = await SecureStorage.Default.GetAsync(TokenKey);
        return _cachedToken;
    }

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

    public async Task<string?> GetRefreshTokenAsync()
    {
        if (_cachedRefreshToken is not null)
            return _cachedRefreshToken;
        _cachedRefreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        return _cachedRefreshToken;
    }

    public async Task<bool> IsLoggedInAsync() =>
        await GetTokenAsync() is not null;

    public static bool IsGuest() => Preferences.Default.Get("is_guest", false);

    public static void SetGuest() => Preferences.Default.Set("is_guest", true);

    public static void ClearGuest() => Preferences.Default.Remove("is_guest");

    public bool GetRememberMe() => Preferences.Default.Get("remember_me", false);

    private async Task StoreRememberMeAsync(bool rememberMe)
    {
        if (rememberMe)
            Preferences.Default.Set("remember_me", true);
        else
            Preferences.Default.Remove("remember_me");
    }

    private static Task<string?> TryReadProblemAsync(HttpResponseMessage response)
        => HttpHelper.TryReadProblemAsync(response);

    private class JwtPayloadReader(string token)
    {
        private readonly JsonElement _payload = ParsePayload(token);

        private static JsonElement ParsePayload(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return default;

            var json = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(Base64Helper.PadBase64(parts[1])));

            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        public string? GetGivenName() =>
            _payload.TryGetProperty("given_name", out var name) ? name.GetString() : null;

        public string? GetEmail() =>
            _payload.TryGetProperty("email", out var email) ? email.GetString() : null;
    }
}
