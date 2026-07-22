using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Services;

public class DynamicClaimsTransformation : IClaimsTransformation
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IMemoryCache _cache;

    private record CachedClaims(List<string> Roles, List<string> Permissions);

    public DynamicClaimsTransformation(UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager, IMemoryCache cache)
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
            entry.SlidingExpiration = TimeSpan.FromSeconds(30);

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
