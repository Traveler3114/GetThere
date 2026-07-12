using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public class AdminManager
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

public AdminManager(AppDbContext db, UserManager<AppUser> userManager) { _db = db; _userManager = userManager; }

    public async Task<PagedResult<UserListItem>> GetUsersAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _userManager.Users.OrderBy(u => u.Email);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserListItem
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName,
                CreatedAt = u.CreatedAt,
                LastLogin = u.LastLogin
            })
            .ToListAsync(ct);

        return new PagedResult<UserListItem>(items, totalCount)
        {
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<AuditLogEntry>> GetAuditLogsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var query = _db.AuditLogs
            .Include(al => al.User)
            .OrderByDescending(al => al.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(al => new AuditLogEntry
            {
                Id = al.Id,
                UserId = al.UserId,
                UserEmail = al.User != null ? al.User.Email : null,
                Action = al.Action,
                EntityType = al.EntityType,
                EntityId = al.EntityId,
                OldValues = al.OldValues,
                NewValues = al.NewValues,
                CreatedAt = al.CreatedAt
            })
            .ToListAsync(ct);

        return new PagedResult<AuditLogEntry>(items, totalCount)
        {
            Page = page,
            PageSize = pageSize
        };
    }
}
