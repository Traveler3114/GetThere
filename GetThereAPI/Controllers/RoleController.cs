using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GetThereAPI.Managers;
using GetThereAPI.Common;

namespace GetThereAPI.Controllers;

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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
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

public record CreateRoleRequest(string Name, List<string> Permissions);
public record UpdateRolePermissionsRequest(List<string> Permissions);

public class RoleDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = [];
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
    public bool IsActive { get; set; }
}