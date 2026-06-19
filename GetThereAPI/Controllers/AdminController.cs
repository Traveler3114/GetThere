using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly AdminManager _adminManager;

    public AdminController(AdminManager adminManager)
    {
        _adminManager = adminManager;
    }

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<UserListItem>>> GetUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _adminManager.GetUsersAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("audit")]
    public async Task<ActionResult<PagedResult<AuditLogEntry>>> GetAuditLogs(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _adminManager.GetAuditLogsAsync(page, pageSize);
        return Ok(result);
    }
}
