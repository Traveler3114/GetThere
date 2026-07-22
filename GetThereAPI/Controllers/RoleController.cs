using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GetThereAPI.Managers;
using GetThereShared.Contracts;
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

}

public record CreateRoleRequest(string Name, List<string> Permissions);
public record UpdateRolePermissionsRequest(List<string> Permissions);

