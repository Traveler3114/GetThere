using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class RouteManager
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);
    private readonly TransitDbContext _db;
    private readonly IConfiguration _config;

    public RouteManager(TransitDbContext db, IConfiguration config) { _db = db; _config = config; }

    public async Task<List<RouteResponse>> GetAllAsync(int? operatorId, RouteType? routeType, string? q, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable().AsNoTracking();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.LongName.Contains(q) || r.ShortName.Contains(q));

        return await query.OrderBy(r => r.Id).Skip((page - 1) * perPage).Take(perPage).Select(RouteMapper.ToResponseExpression).ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(int? operatorId, RouteType? routeType, string? q, CancellationToken ct = default)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable().AsNoTracking();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.LongName.Contains(q) || r.ShortName.Contains(q));

        return await query.CountAsync(ct);
    }

    public async Task<object> GetAllGeoJsonAsync(
        int? operatorId, RouteType? routeType,
        double? minLat, double? minLon, double? maxLat, double? maxLon,
        int limit, CancellationToken ct)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);

        if (minLat.HasValue && minLon.HasValue && maxLat.HasValue && maxLon.HasValue)
        {
            var envelope = new Envelope(minLon.Value, maxLon.Value, minLat.Value, maxLat.Value);
            var bbox = GeometryFactory.ToGeometry(envelope);
            if (bbox is Polygon poly && !Orientation.IsCCW(poly.Shell.Coordinates))
                bbox = poly.Reverse();
            query = query.Where(r => r.Geometry != null && r.Geometry.Intersects(bbox));
        }

        var routes = await query.OrderBy(r => r.Id).Take(limit).ToListAsync(ct);

        return GeoJsonGeometry.ToLineStringCollection(routes,
            r => r.Geometry,
            r => new Dictionary<string, object?>
            {
                ["id"] = r.Id,
                ["onestopId"] = r.OnestopId,
                ["name"] = r.LongName,
                ["shortName"] = r.ShortName,
                ["routeType"] = r.RouteType.ToString(),
                ["operatorId"] = r.OperatorId,
                ["shapeEdited"] = r.ShapeEdited
            });
    }

    public async Task<RouteResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.CanonicalRoutes
            .Where(r => r.Id == id)
            .Select(RouteMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RouteResponse?> GetByOnestopIdAsync(string onestopId, CancellationToken ct)
    {
        return await _db.CanonicalRoutes
            .Where(r => r.OnestopId == onestopId)
            .Select(RouteMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CanonicalRoute?> GetEntityByIdAsync(int id, CancellationToken ct)
    {
        return await _db.CanonicalRoutes.FindAsync([id], ct);
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

    public async Task<RouteResponse?> UpdateShapeAsync(int id, GeoJsonLineStringGeometry body, CancellationToken ct)
    {
        var route = await _db.CanonicalRoutes.FindAsync([id], ct);
        if (route is null) return null;

        var shape = await GetActiveShapeForRouteAsync(id, ct);
        if (shape is null) return null;

        var coords = body.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
        if (coords.Length < 2)
            return null;

        shape.Geometry = GeometryFactory.CreateLineString(coords);
        shape.IsManuallyEdited = true;

        route.Geometry = shape.Geometry;
        route.ShapeEdited = true;

        await _db.SaveChangesAsync(ct);

        return RouteMapper.ToResponse(route);
    }

    public async Task<Geometry?> GetShapeGeometryAsync(int id, CancellationToken ct)
    {
        return await _db.CanonicalRoutes
            .Where(r => r.Id == id)
            .Select(r => r.Geometry)
            .FirstOrDefaultAsync(ct);
    }
}
