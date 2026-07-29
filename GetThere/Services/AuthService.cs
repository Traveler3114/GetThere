using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class AuthService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedToken;
    private string? _cachedRefreshToken;
    public const string TokenKey = "auth_token";
    public const string RefreshTokenKey = "refresh_token";

    public AuthService(HttpClient http) { _http = http; }

    /// <summary>
    /// Registered as a singleton, so in practice this runs only at shutdown — but the refresh lock
    /// and the owned HttpClient are disposable, and leaving them undisposed trips CA1001.
    /// </summary>
    public void Dispose()
    {
        _refreshLock.Dispose();
        _http.Dispose();
        GC.SuppressFinalize(this);
    }

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
        var observedRefreshToken = await GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(observedRefreshToken)) return false;

        // Refresh is serialized. The server rotates the refresh token and revokes the old one, so two
        // concurrent requests presenting the same token would leave the second one holding a replayed
        // token — which trips reuse detection and signs the user out mid-session.
        await _refreshLock.WaitAsync();
        try
        {
            var currentRefreshToken = await GetRefreshTokenAsync();
            if (string.IsNullOrWhiteSpace(currentRefreshToken)) return false;

            // Someone else refreshed while we waited; their new token is already stored.
            if (currentRefreshToken != observedRefreshToken)
                return true;

            var response = await _http.PostAsJsonAsync("auth/refresh", new RefreshTokenRequest(currentRefreshToken), JsonOptions);

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
        finally
        {
            _refreshLock.Release();
        }
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

    /// <summary>
    /// Whether the app currently holds usable credentials.
    /// <para>
    /// This used to answer "is there a token", which is true of an expired one too — so a user
    /// returning after their session lapsed was treated as signed in, and found out only when the
    /// next request came back 401. An expired access token with a live refresh token is a recoverable
    /// state rather than a signed-out one, so it is refreshed here instead of being reported as
    /// either.
    /// </para>
    /// </summary>
    public async Task<bool> IsLoggedInAsync()
    {
        var token = await GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (!IsExpired(token)) return true;

        return await TryRefreshTokenAsync();
    }

    /// <summary>
    /// Whether the token's own <c>exp</c> claim has passed. A token whose expiry cannot be read is
    /// treated as still valid: the 401 path in <c>AuthenticatedHttpHandler</c> is the backstop, and
    /// signing someone out over an unparseable claim is the worse error.
    /// </summary>
    private static bool IsExpired(string token)
    {
        try
        {
            return new JwtPayloadReader(token).GetExpiry() is { } expiry && expiry <= DateTimeOffset.UtcNow;
        }
        catch
        {
            return false;
        }
    }

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

    private sealed class JwtPayloadReader(string token)
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

        /// <summary>The token's expiry, or null when it carries no readable <c>exp</c> claim.</summary>
        public DateTimeOffset? GetExpiry() =>
            _payload.ValueKind == JsonValueKind.Object
            && _payload.TryGetProperty("exp", out var exp)
            && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
    }
}
