using System.Security.Claims;

namespace GetThereAPI.Common;

/// <summary>
/// Pulls the authenticated user's id off the principal. Every user-scoped controller action needs
/// it as the first thing it does, and the lookup plus null-check was copy-pasted into each one.
/// </summary>
public static class UserClaimExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirst(JwtClaimTypes.UserId)?.Value;
}
