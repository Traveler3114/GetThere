using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Common;
using GetThereShared.Contracts;
using GetThereAPI.Exceptions;

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

    private string? CurrentUserId => _httpContext.HttpContext?.User.FindFirst(JwtClaimTypes.UserId)?.Value;

    public async Task<List<RoleDto>> GetAllRolesAsync(CancellationToken ct = default)
    {
        var roles = await _roleManager.Roles.AsNoTracking().ToListAsync(ct);
        var roleIds = roles.Select(r => r.Id).ToList();
        var roleClaims = await _db.Set<IdentityRoleClaim<string>>()
            .Where(rc => roleIds.Contains(rc.RoleId) && rc.ClaimType == "permission")
            .ToListAsync(ct);
        var claimsByRole = roleClaims.GroupBy(rc => rc.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.ClaimValue).ToList());

        return roles.Select(role => new RoleDto
        {
            Name = role.Name!,
            Permissions = claimsByRole.TryGetValue(role.Id, out var perms) ? perms : []
        }).ToList();
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
        if (!result.Succeeded) throw new AppException(string.Join(", ", result.Errors.Select(e => e.Description)), 400);

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
        if (role is null) throw new AppException("Role not found", 404);

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
            throw new AppException("Cannot delete built-in role", 400);

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
        var query = _userManager.Users.OrderBy(u => u.Email).AsNoTracking();
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToList();

        var userRoles = await _db.Set<IdentityUserRole<string>>()
            .Where(ur => userIds.Contains(ur.UserId))
            .ToListAsync(ct);
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name!, ct);
        var rolesByUser = userRoles.GroupBy(ur => ur.UserId)
            .ToDictionary(g => g.Key, g => g.Select(ur => roleNames.GetValueOrDefault(ur.RoleId, "Unknown")).ToList());

        return users.Select(user => new UserDto
        {
            Id = user.Id,
            Email = user.Email!,
            FullName = user.FullName ?? string.Empty,
            Roles = rolesByUser.TryGetValue(user.Id, out var roles) ? roles : [],
            CreatedAt = user.CreatedAt,
            LastLogin = user.LastLogin,
            IsActive = true
        }).ToList();
    }

    public async Task<AppUser?> SetUserRoleAsync(string userId, string roleName, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        var currentRoles = await _userManager.GetRolesAsync(user);

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "SetUserRole",
            EntityType = "User",
            EntityId = userId,
            OldValues = string.Join(", ", currentRoles),
            NewValues = roleName,
            CreatedAt = DateTime.UtcNow
        });

        if (currentRoles.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

        await _userManager.AddToRoleAsync(user, roleName);
        return user;
    }
}