using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly UserSettingsManager _settingsManager;

    public SettingsController(UserSettingsManager settingsManager) { _settingsManager = settingsManager; }

    [HttpGet]
    public async Task<ActionResult<UserSettingsResponse>> Get(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _settingsManager.GetSettingsAsync(userId, ct);
        return Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        await _settingsManager.UpdateSettingsAsync(userId, request, ct);
        return NoContent();
    }
}
