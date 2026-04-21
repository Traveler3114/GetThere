using System.Net.Http.Headers;
using GetThere.Services;

namespace GetThere.Helpers;

// A DelegatingHandler sits "in the middle" of every HTTP request
// Think of it like middleware, but for HttpClient
public class AuthenticatedHttpHandler : DelegatingHandler
{
    private readonly AuthService _authService;
    private static readonly HttpRequestOptionsKey<bool> AlreadyRetriedAfterRefreshKey = new("AlreadyRetriedAfterRefresh");

    public AuthenticatedHttpHandler(AuthService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Grab the saved token from SecureStorage
        var token = await _authService.GetTokenAsync();

        // If we have one, attach it as a Bearer header
        // This is what the API reads when it sees [Authorize]
        // It becomes: Authorization: Bearer eyJhbGci....
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Continue sending the request as normal
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        if (request.Options.TryGetValue(AlreadyRetriedAfterRefreshKey, out var alreadyRetried) && alreadyRetried)
            return response;

        var refreshed = await _authService.TryRefreshTokenAsync();
        if (!refreshed)
        {
            _authService.Logout();
            MainThread.BeginInvokeOnMainThread(App.GoToLogin);
            return response;
        }

        byte[]? requestBodyBytes = null;
        if (request.Content != null)
            requestBodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var clonedRequest = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        if (requestBodyBytes != null)
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
}
