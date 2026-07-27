using System.ComponentModel.DataAnnotations;

using GetThereAPI.Common;
using GetThereAPI.Managers;

using GetThereShared.Common;
using GetThereShared.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

public record SetRoleRequest(string RoleName);

public record SetAdapterActiveRequest(bool IsActive);

[ApiController]
[Route("[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly AdminManager _adminManager;
    private readonly RolePermissionManager _rolePermissionManager;

    public AdminController(AdminManager adminManager, RolePermissionManager rolePermissionManager) { _adminManager = adminManager; _rolePermissionManager = rolePermissionManager; }

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
        var user = await _rolePermissionManager.SetUserRoleAsync(userId, request.RoleName, ct);
        if (user is null) return NotFound();
        return Ok(new { message = $"User role set to '{request.RoleName}'." });
    }

    [HttpGet("stats")]
    [Authorize(Policy = PermissionKeys.AdminStatsView)]
    public async Task<ActionResult<AdminStats>> GetStats(
        [FromQuery, Range(1, 720)] int windowHours = 24, CancellationToken ct = default)
    {
        var result = await _adminManager.GetStatsAsync(windowHours, ct);
        return Ok(result);
    }

    [HttpGet("purchases")]
    [Authorize(Policy = PermissionKeys.AdminPurchasesView)]
    public async Task<ActionResult<PagedResult<PurchaseListItem>>> GetPurchases(
        [FromQuery] string? status = null,
        [FromQuery] int? adapterId = null,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _adminManager.GetPurchasesAsync(status, adapterId, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("adapters")]
    [Authorize(Policy = PermissionKeys.AdaptersView)]
    public async Task<ActionResult<List<AdapterHealthItem>>> GetAdapters(
        [FromQuery, Range(1, 720)] int windowHours = 24, CancellationToken ct = default)
    {
        var result = await _adminManager.GetAdapterHealthAsync(windowHours, ct);
        return Ok(result);
    }

    [HttpPatch("adapters/{adapterId:int}")]
    [Authorize(Policy = PermissionKeys.AdaptersManage)]
    public async Task<ActionResult<AdapterHealthItem>> SetAdapterActive(
        int adapterId, [FromBody] SetAdapterActiveRequest request, CancellationToken ct = default)
    {
        var result = await _adminManager.SetAdapterActiveAsync(adapterId, request.IsActive, ct);
        if (result is null) return NotFound();
        return Ok(result);
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
