using GetThereAPI.Entities;
using GetThereAPI.Exceptions;

using GetThereShared.Contracts;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class ProfileManager
{
    private readonly UserManager<AppUser> _userManager;

    public ProfileManager(UserManager<AppUser> userManager) { _userManager = userManager; }

    public async Task<AppUser?> GetUserByIdAsync(string userId, CancellationToken ct = default)
    {
        return await _userManager.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    public async Task UpdateProfileAsync(AppUser user, UpdateProfileRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName;

        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
        {
            var setEmailResult = await _userManager.SetEmailAsync(user, request.Email);
            if (!setEmailResult.Succeeded)
                throw new AppException(string.Join(", ", setEmailResult.Errors.Select(e => e.Description)));

            // The username has to move with the address. Registration sets UserName = Email, and
            // SetEmailAsync does not touch UserName — so without this the account keeps its old
            // address as a username forever. That is not cosmetic: a later registration for the old
            // address passes the FindByEmailAsync check, then fails CreateAsync with
            // DuplicateUserName, which AuthManager deliberately reports as success so registration
            // is not an account oracle. The result is a caller told they registered when no account
            // exists, permanently, for that address.
            var setUserNameResult = await _userManager.SetUserNameAsync(user, request.Email);
            if (!setUserNameResult.Succeeded)
                throw new AppException(string.Join(", ", setUserNameResult.Errors.Select(e => e.Description)));
        }
        else
        {
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                throw new AppException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
