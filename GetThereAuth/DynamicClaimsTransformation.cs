using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace GetThereAuth;

/// <summary>
/// Replaces the role and permission claims carried in the token with what the database says right
/// now, so a revoked role stops working without waiting for the access token to expire.
/// <para>
/// Both APIs had their own copy. They had already drifted — one wrote a cache entry size and the
/// other did not — and only one of them was safe to use with a size-limited cache.
/// </para>
/// </summary>
public class DynamicClaimsTransformation<TUser> : IClaimsTransformation
    where TUser : IdentityUser
{
    private readonly UserManager<TUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Hard ceiling on the claims cache. Sliding expiration alone never lapses for a user who keeps
    /// making requests, which would let a revoked role or permission stay live indefinitely.
    /// </summary>
    private static readonly TimeSpan CacheAbsoluteExpiration = TimeSpan.FromMinutes(5);

    private sealed record CachedClaims(List<string> Roles, List<string> Permissions);

    public DynamicClaimsTransformation(UserManager<TUser> userManager, RoleManager<IdentityRole> roleManager, IMemoryCache cache)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _cache = cache;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        var userId = principal.FindFirst("sub")?.Value;
        if (userId is null)
            return principal;

        var identity = (ClaimsIdentity)principal.Identity!;

        var oldRoleClaims = identity.FindAll("role").ToList();
        var oldPermissionClaims = identity.FindAll("permission").ToList();

        foreach (var c in oldRoleClaims.Concat(oldPermissionClaims))
            identity.RemoveClaim(c);

        var cached = await _cache.GetOrCreateAsync($"claims:{userId}", async entry =>
        {
            entry.SlidingExpiration = CacheSlidingExpiration;
            entry.AbsoluteExpirationRelativeToNow = CacheAbsoluteExpiration;

            // Harmless where the cache has no SizeLimit, and required where it does: a size-limited
            // cache throws on any entry that does not declare one.
            entry.Size = 1;

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return null;

            var roles = (await _userManager.GetRolesAsync(user)).ToList();
            var perms = new List<string>();

            foreach (var roleName in roles)
            {
                var roleEntity = await _roleManager.FindByNameAsync(roleName);
                if (roleEntity is not null)
                {
                    var roleClaims = await _roleManager.GetClaimsAsync(roleEntity);
                    perms.AddRange(roleClaims.Where(c => c.Type == "permission").Select(c => c.Value));
                }
            }

            return new CachedClaims(roles, perms);
        });

        if (cached is not null)
        {
            foreach (var role in cached.Roles)
                identity.AddClaim(new Claim("role", role));

            foreach (var perm in cached.Permissions)
                identity.AddClaim(new Claim("permission", perm));
        }

        return principal;
    }
}
