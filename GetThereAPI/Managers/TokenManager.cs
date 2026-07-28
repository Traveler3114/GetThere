using GetThereAPI.Entities;

using Microsoft.AspNetCore.Identity;

namespace GetThereAPI.Managers;

/// <summary>
/// This API's binding of the shared <see cref="GetThereAuth.TokenManager{TUser}"/>.
/// <para>
/// The body used to live here in full, alongside a near-identical copy in TransitInfoAPI. The type
/// stays in this namespace so the manager auto-registration in <c>Program.cs</c> still finds it and
/// no call site changes.
/// </para>
/// </summary>
public class TokenManager : GetThereAuth.TokenManager<AppUser>
{
    public TokenManager(IConfiguration config, UserManager<AppUser> userManager)
        : base(config, userManager) { }
}
