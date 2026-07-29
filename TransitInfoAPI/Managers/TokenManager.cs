using Microsoft.AspNetCore.Identity;

using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Managers;

/// <summary>
/// This API's binding of the shared <see cref="SharedAuth.TokenManager{TUser}"/>.
/// <para>
/// The body used to live here in full, alongside a near-identical copy in GetThereAPI. The type
/// stays in this namespace so the explicit registration in <c>Program.cs</c> and every call site
/// are unchanged.
/// </para>
/// </summary>
public class TokenManager : SharedAuth.TokenManager<AppUser>
{
    public TokenManager(IConfiguration config, UserManager<AppUser> userManager)
        : base(config, userManager) { }
}
