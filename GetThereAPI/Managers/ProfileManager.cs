using Microsoft.AspNetCore.Identity;

using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public class ProfileManager
{
    private readonly UserManager<AppUser> _userManager;

    public ProfileManager(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<AppUser?> GetUserByIdAsync(string userId)
    {
        return await _userManager.FindByIdAsync(userId);
    }

    public async Task UpdateProfileAsync(AppUser user, UpdateProfileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName;

        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
        {
            var setEmailResult = await _userManager.SetEmailAsync(user, request.Email);
            if (!setEmailResult.Succeeded)
                throw new AppException(string.Join(", ", setEmailResult.Errors.Select(e => e.Description)));
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new AppException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}
