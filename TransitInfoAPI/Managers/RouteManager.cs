using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class RouteManager
{
    private readonly TransitDbContext _db;

    public RouteManager(TransitDbContext db)
    {
        _db = db;
    }

    public async Task<List<RouteResponse>> GetAllAsync(int? operatorId, RouteType? routeType, string? q, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.LongName.Contains(q) || r.ShortName.Contains(q));

        return await query.OrderBy(r => r.Id).Skip((page - 1) * perPage).Take(perPage).Select(RouteMapper.ToResponseExpression).ToListAsync(ct);
    }

}
