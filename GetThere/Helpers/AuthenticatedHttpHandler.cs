using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using GetThere.Services;

using GetThereShared.Common;

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
            // "Could not refresh" now covers two very different cases, because TryRefreshTokenAsync
            // reports a transport failure as false rather than throwing. A rejected refresh token
            // means sign the user out; an unreachable server means the connection dropped between
            // the 401 and the refresh, and wiping their credentials over that would be a bad trade.
            // Leave them signed in and let the caller surface an ordinary failure.
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                Trace.WriteLine("[AuthenticatedHttpHandler] Refresh failed with no connection; keeping the session.");
                return response;
            }

            await _authService.Logout();
            MainThread.BeginInvokeOnMainThread(App.GoToLogin);
            return response;
        }

        byte[]? requestBodyBytes = null;
        if (request.Content is not null)
        {
            try
            {
                requestBodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or NotSupportedException)
            {
                // The body was a one-shot stream and the first send consumed it, so there is nothing
                // to replay. Returning the 401 is the honest outcome — retrying with an empty or
                // truncated body would look like a successful upload of the wrong bytes. Callers
                // should buffer content they need to be retryable, as ImportedTicketService does.
                Trace.WriteLine($"[AuthenticatedHttpHandler] Request body cannot be replayed, not retrying after 401: {ex.Message}");
                return response;
            }
        }

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
                Convert.FromBase64String(Base64Helper.PadBase64(parts[1])));
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("exp", out var expProp) &&
                expProp.TryGetInt64(out var expSeconds))
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                return expiry <= DateTimeOffset.UtcNow.Add(TokenRefreshBuffer);
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            // Returning false here means no pre-emptive refresh — the request goes out with the
            // token as-is and relies on the 401 retry below. Worth knowing about, so it is logged
            // rather than silently swallowed.
            Trace.WriteLine($"[AuthenticatedHttpHandler] Could not read token expiry: {ex.Message}");
        }

        return false;
    }

}
