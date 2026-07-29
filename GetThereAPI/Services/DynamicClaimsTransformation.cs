using GetThereAPI.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace GetThereAPI.Services;

/// <summary>
/// This API's binding of the shared <see cref="SharedAuth.DynamicClaimsTransformation{TUser}"/>,
/// which replaces token-carried role and permission claims with what the database says now.
/// </summary>
public class DynamicClaimsTransformation : SharedAuth.DynamicClaimsTransformation<AppUser>
{
    public DynamicClaimsTransformation(
        UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager, IMemoryCache cache)
        : base(userManager, roleManager, cache) { }
}
