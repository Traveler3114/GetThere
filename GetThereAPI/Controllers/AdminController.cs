using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereAPI.Common;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public record SetRoleRequest(string RoleName);

public class AdminController : ControllerBase
{
    private readonly AdminManager _adminManager;

public AdminController(AdminManager adminManager) { _adminManager = adminManager; }

    [HttpGet("users")]
    [Authorize(Policy = PermissionKeys.UsersView)]
    public async Task<ActionResult<PagedResult<UserListItem>>> GetUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _adminManager.GetUsersAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost("users/{userId}/role")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public async Task<ActionResult> SetUserRole(string userId, [FromBody] SetRoleRequest request, CancellationToken ct = default)
    {
        var user = await _adminManager.SetUserRoleAsync(userId, request.RoleName, ct);
        if (user is null) return NotFound();
        return Ok(new { message = $"User role set to '{request.RoleName}'." });
    }

    [HttpGet("audit")]
    [Authorize(Policy = PermissionKeys.AuditView)]
    public async Task<ActionResult<PagedResult<AuditLogEntry>>> GetAuditLogs(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _adminManager.GetAuditLogsAsync(page, pageSize, ct);
        return Ok(result);
    }
}