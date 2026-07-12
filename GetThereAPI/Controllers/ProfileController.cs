using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereAPI.Mapping;
using GetThereShared.Contracts;
using GetThereAPI.Common;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly ProfileManager _profileManager;

public ProfileController(ProfileManager profileManager) { _profileManager = profileManager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.ProfileView)]
    public async Task<ActionResult<UserResponse>> Get(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var user = await _profileManager.GetUserByIdAsync(userId, ct);
        if (user is null) return Problem(statusCode: 404, title: "User not found.");

        return Ok(AuthMapper.ToResponse(user));
    }

    [HttpPut]
    [Authorize(Policy = PermissionKeys.ProfileManage)]
    public async Task<ActionResult> Update(
        [FromBody] UpdateProfileRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var user = await _profileManager.GetUserByIdAsync(userId, ct);
        if (user is null) return Problem(statusCode: 404, title: "User not found.");

        await _profileManager.UpdateProfileAsync(user, request, ct);

        return NoContent();
    }
}