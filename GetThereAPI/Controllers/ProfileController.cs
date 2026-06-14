using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly ProfileManager _profileManager;

    public ProfileController(ProfileManager profileManager)
    {
        _profileManager = profileManager;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<UserResponse>>> Get(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var user = await _profileManager.GetUserByIdAsync(userId);
        if (user is null) return NotFound(OperationResult<UserResponse>.Fail("User not found."));

        return Ok(OperationResult<UserResponse>.Ok(_profileManager.ToResponse(user)));
    }

    [HttpPut]
    public async Task<ActionResult<OperationResult<UserResponse>>> Update(
        [FromBody] UpdateProfileRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var user = await _profileManager.GetUserByIdAsync(userId);
        if (user is null) return NotFound(OperationResult<UserResponse>.Fail("User not found."));

        var (success, error) = await _profileManager.UpdateProfileAsync(user, request);
        if (!success)
            return BadRequest(OperationResult<UserResponse>.Fail(error!));

        return Ok(OperationResult<UserResponse>.Ok(_profileManager.ToResponse(user)));
    }
}
