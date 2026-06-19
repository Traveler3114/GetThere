using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Managers;

public class RouteManager
{
    private readonly TransitDbContext _db;

    public RouteManager(TransitDbContext db)
    {
        _db = db;
    }

    public async Task<List<RouteDto>> GetAllAsync(int? operatorId, RouteType? routeType, string? q, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.LongName.Contains(q) || r.ShortName.Contains(q));

        return await query.OrderBy(r => r.Id).Skip((page - 1) * perPage).Take(perPage).Select(r => new RouteDto
        {
            Id = r.Id,
            GlobalId = r.GlobalId,
            OnestopId = r.OnestopId,
            Name = r.LongName,
            ShortName = r.ShortName,
            RouteType = r.RouteType.ToString(),
            OperatorId = r.OperatorId,
            OperatorName = r.Operator.Name
        }).ToListAsync(ct);
    }

    public async Task<RouteDto?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.CanonicalRoutes
            .Where(r => r.GlobalId == globalId && r.IsActive)
            .Select(r => new RouteDto
            {
                Id = r.Id,
                GlobalId = r.GlobalId,
                OnestopId = r.OnestopId,
                Name = r.LongName,
                ShortName = r.ShortName,
                RouteType = r.RouteType.ToString(),
                OperatorId = r.OperatorId,
                OperatorName = r.Operator.Name
            })
            .FirstOrDefaultAsync(ct);
    }
}
