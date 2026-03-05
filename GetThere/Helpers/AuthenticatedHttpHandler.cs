using System.Net.Http.Headers;
using GetThere.Services;

namespace GetThere.Helpers;

// A DelegatingHandler sits "in the middle" of every HTTP request
// Think of it like middleware, but for HttpClient
public class AuthenticatedHttpHandler : DelegatingHandler
{
    private readonly AuthService _authService;

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
        return await base.SendAsync(request, cancellationToken);
    }
}