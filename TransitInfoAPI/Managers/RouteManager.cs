using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Geometries;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
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

    public async Task<Shape?> GetActiveShapeForRouteAsync(int canonicalRouteId, CancellationToken ct)
    {
        var shapeCounts = await _db.Trips
            .Where(t => t.CanonicalRouteId == canonicalRouteId && t.FeedVersion.IsActive && t.ShapeId != null)
            .GroupBy(t => t.ShapeId)
            .Select(g => new { ShapeId = g.Key!, Count = g.Count() })
            .ToListAsync(ct);

        if (shapeCounts.Count == 0) return null;

        var mostCommonShapeId = shapeCounts.OrderByDescending(x => x.Count).Select(x => x.ShapeId).First();

        return await _db.Shapes
            .Where(s => s.ShapeId == mostCommonShapeId && s.FeedVersion.IsActive)
            .FirstOrDefaultAsync(ct);
    }

}
