using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Common;

namespace GetThereAPI.Managers;

public class RolePermissionManager
{
    private readonly AppDbContext _db;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly IHttpContextAccessor _httpContext;

    public RolePermissionManager(AppDbContext db, RoleManager<IdentityRole> roleManager, UserManager<AppUser> userManager, IHttpContextAccessor httpContext)
    {
        _db = db;
        _roleManager = roleManager;
        _userManager = userManager;
        _httpContext = httpContext;
    }

    private string? CurrentUserId => _httpContext.HttpContext?.User.FindFirst("sub")?.Value;

    public async Task<List<RoleDto>> GetAllRolesAsync(CancellationToken ct = default)
    {
        var roles = await _roleManager.Roles.ToListAsync(ct);
        var result = new List<RoleDto>();

        foreach (var role in roles)
        {
            var claims = await _roleManager.GetClaimsAsync(role);
            var permissions = claims.Where(c => c.Type == "permission").Select(c => c.Value).ToList();
            result.Add(new RoleDto { Name = role.Name!, Permissions = permissions });
        }
        return result;
    }

    public async Task<RoleDto?> GetRoleAsync(string name, CancellationToken ct = default)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role is null) return null;

        var claims = await _roleManager.GetClaimsAsync(role);
        return new RoleDto { Name = role.Name!, Permissions = claims.Where(c => c.Type == "permission").Select(c => c.Value).ToList() };
    }

    public async Task<IdentityRole> CreateRoleAsync(string name, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        var role = new IdentityRole { Name = name };
        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded) throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));

        foreach (var perm in permissions)
            await _roleManager.AddClaimAsync(role, new Claim("permission", perm));

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "CreateRole",
            EntityType = "Role",
            EntityId = role.Name,
            NewValues = string.Join(", ", permissions),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return role;
    }

    public async Task UpdateRolePermissionsAsync(string name, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role is null) throw new Exception("Role not found");

        var existingClaims = await _roleManager.GetClaimsAsync(role);

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "UpdateRolePermissions",
            EntityType = "Role",
            EntityId = role.Name ?? name,
            OldValues = string.Join(", ", existingClaims.Where(c => c.Type == "permission").Select(c => c.Value)),
            NewValues = string.Join(", ", permissions),
            CreatedAt = DateTime.UtcNow
        });

        foreach (var claim in existingClaims.Where(c => c.Type == "permission"))
            await _roleManager.RemoveClaimAsync(role, claim);

        foreach (var perm in permissions)
            await _roleManager.AddClaimAsync(role, new Claim("permission", perm));

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteRoleAsync(string name, CancellationToken ct = default)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role is null) return;

        if (name == RoleNames.Admin || name == RoleNames.User)
            throw new Exception("Cannot delete built-in role");

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "DeleteRole",
            EntityType = "Role",
            EntityId = role.Name ?? name,
            CreatedAt = DateTime.UtcNow
        });

        await _roleManager.DeleteAsync(role);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<UserDto>> GetUsersAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var query = _userManager.Users.OrderBy(u => u.Email);
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var result = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName ?? string.Empty,
                Roles = roles.ToList(),
                CreatedAt = user.CreatedAt,
                LastLogin = user.LastLogin,
                IsActive = true
            });
        }
        return result;
    }

    public async Task<AppUser?> SetUserRoleAsync(string userId, string roleName, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

        await _userManager.AddToRoleAsync(user, roleName);
        return user;
    }
}

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