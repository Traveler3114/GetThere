using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereShared.Contracts;

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
        }
        else
        {
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                throw new AppException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
