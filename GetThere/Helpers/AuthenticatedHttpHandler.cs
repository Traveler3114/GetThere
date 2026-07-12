using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using GetThere.Services;

namespace GetThere.Helpers;

public class AuthenticatedHttpHandler : DelegatingHandler
{
    private readonly AuthService _authService;
    private static readonly HttpRequestOptionsKey<bool> AlreadyRetriedAfterRefreshKey = new("AlreadyRetriedAfterRefresh");
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    public AuthenticatedHttpHandler(AuthService authService) { _authService = authService; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authService.GetTokenAsync();

        if (!string.IsNullOrEmpty(token))
        {
            if (IsTokenExpiringSoon(token))
            {
                Trace.WriteLine("[AuthenticatedHttpHandler] Token expiring soon, pre-emptively refreshing");
                var refreshed = await _authService.TryRefreshTokenAsync();
                if (refreshed)
                    token = await _authService.GetTokenAsync();
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        if (request.Options.TryGetValue(AlreadyRetriedAfterRefreshKey, out var alreadyRetried) && alreadyRetried)
            return response;

        var refreshedAfter401 = await _authService.TryRefreshTokenAsync();
        if (!refreshedAfter401)
        {
            await _authService.Logout();
            MainThread.BeginInvokeOnMainThread(App.GoToLogin);
            return response;
        }

        byte[]? requestBodyBytes = null;
        if (request.Content is not null)
            requestBodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var clonedRequest = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        if (requestBodyBytes is not null)
        {
            clonedRequest.Content = new ByteArrayContent(requestBodyBytes);
            foreach (var header in request.Content!.Headers)
                clonedRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in request.Headers)
            clonedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);

        clonedRequest.Options.Set(AlreadyRetriedAfterRefreshKey, true);

        var newToken = await _authService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(newToken))
            clonedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);

        return await base.SendAsync(clonedRequest, cancellationToken);
    }

    private static bool IsTokenExpiringSoon(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return false;

            var payload = Encoding.UTF8.GetString(
                Convert.FromBase64String(PadBase64(parts[1])));
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("exp", out var expProp) &&
                expProp.TryGetInt64(out var expSeconds))
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                return expiry <= DateTimeOffset.UtcNow.Add(TokenRefreshBuffer);
            }
        }
        catch { }

        return false;
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
}
