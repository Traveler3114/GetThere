using System.Security.Claims;

using GetThereAPI.Common;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;

using GetThereShared.Contracts;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
            .ToDictionary(g => g.Key, g => g.Select(c => c.ClaimValue!).ToList());

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

        // Wrapped, because this is a remove-all-then-add-all with a save behind every step. A
        // failure between the two loops previously left the role stripped of every permission —
        // applied to Admin, that is a lockout of every permission-gated endpoint, and
        // DynamicClaimsTransformation re-reads role claims per request, so it takes hold within the
        // 30-second cache window rather than at next sign-in.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (var claim in existingClaims.Where(c => c.Type == "permission"))
        {
            var removeResult = await _roleManager.RemoveClaimAsync(role, claim);
            if (!removeResult.Succeeded)
                throw new AppException(string.Join(", ", removeResult.Errors.Select(e => e.Description)), 400);
        }

        foreach (var perm in permissions)
        {
            var addResult = await _roleManager.AddClaimAsync(role, new Claim("permission", perm));
            if (!addResult.Succeeded)
                throw new AppException(string.Join(", ", addResult.Errors.Select(e => e.Description)), 400);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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

        // Checked rather than discarded: every foreign key in this model is Restrict
        // (AppDbContext.cs:45-46), so deleting a role that users still hold fails at the database
        // and the caller was told nothing — the console reported a deletion that never happened.
        var deleteResult = await _roleManager.DeleteAsync(role);
        if (!deleteResult.Succeeded)
            throw new AppException(string.Join(", ", deleteResult.Errors.Select(e => e.Description)), 400);

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

        // Checked before anything is removed. This used to strip every existing role and only then
        // call AddToRoleAsync, which throws on a role that does not exist — so a mistyped name left
        // the target account with no roles at all and answered 500. Doing it to your own account
        // locked you out of the console.
        if (!await _roleManager.RoleExistsAsync(roleName))
            throw new AppException($"Role '{roleName}' does not exist.", 400, "ROLE_NOT_FOUND");

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
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
                throw new AppException(string.Join(", ", removeResult.Errors.Select(e => e.Description)), 400);
        }

        var addResult = await _userManager.AddToRoleAsync(user, roleName);
        if (!addResult.Succeeded)
            throw new AppException(string.Join(", ", addResult.Errors.Select(e => e.Description)), 400);

        // Explicit rather than relying on Identity to flush the shared DbContext on its own write,
        // which is what persisted the audit row before.
        await _db.SaveChangesAsync(ct);
        return user;
    }
}
