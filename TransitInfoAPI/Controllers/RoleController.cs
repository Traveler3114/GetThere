using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("admin")]
[Authorize]
public class RoleController : ControllerBase
{
    private readonly RolePermissionManager _roleManager;

    public RoleController(RolePermissionManager roleManager) { _roleManager = roleManager; }

    [HttpGet("roles")]
    [Authorize(Policy = PermissionKeys.RolesView)]
    public async Task<ActionResult<List<RoleDto>>> GetRoles(CancellationToken ct = default)
    {
        return Ok(await _roleManager.GetAllRolesAsync(ct));
    }

    [HttpGet("roles/{name}")]
    [Authorize(Policy = PermissionKeys.RolesView)]
    public async Task<ActionResult<RoleDto>> GetRole(string name, CancellationToken ct = default)
    {
        var role = await _roleManager.GetRoleAsync(name, ct);
        if (role is null) return NotFound();
        return Ok(role);
    }

    [HttpPost("roles")]
    [Authorize(Policy = PermissionKeys.RolesManage)]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] CreateRoleRequest request, CancellationToken ct = default)
    {
        var role = await _roleManager.CreateRoleAsync(request.Name, request.Permissions, ct);
        return Ok(new RoleDto { Name = role.Name!, Permissions = request.Permissions });
    }

    [HttpPut("roles/{name}/permissions")]
    [Authorize(Policy = PermissionKeys.RolesManage)]
    public async Task<ActionResult> UpdateRolePermissions(string name, [FromBody] UpdateRolePermissionsRequest request, CancellationToken ct = default)
    {
        await _roleManager.UpdateRolePermissionsAsync(name, request.Permissions, ct);
        return NoContent();
    }

    [HttpDelete("roles/{name}")]
    [Authorize(Policy = PermissionKeys.RolesManage)]
    public async Task<ActionResult> DeleteRole(string name, CancellationToken ct = default)
    {
        await _roleManager.DeleteRoleAsync(name, ct);
        return NoContent();
    }

    [HttpGet("users")]
    [Authorize(Policy = PermissionKeys.UsersView)]
    public async Task<ActionResult<List<UserDto>>> GetUsers(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        // Bounded like every other paginated endpoint in both services. This was the one that was
        // not, so ?pageSize=1000000 returned the entire user table in a single response.
        [FromQuery, Range(1, 500)] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await _roleManager.GetUsersAsync(page, pageSize, ct));
    }

    [HttpPut("users/{userId}/role")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public async Task<ActionResult> SetUserRole(string userId, [FromBody] SetRoleRequest request, CancellationToken ct = default)
    {
        var user = await _roleManager.SetUserRoleAsync(userId, request.RoleName, ct);
        if (user is null) return NotFound();
        return Ok(new { message = $"User role set to '{request.RoleName}'." });
    }
}
