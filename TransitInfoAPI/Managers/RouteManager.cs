using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class RouteManager
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);
    private readonly TransitDbContext _db;

    public RouteManager(TransitDbContext db) { _db = db; }

    /// <summary>
    /// Resolves feed-version ids to feed slugs in one round trip.
    /// <para>
    /// <c>CanonicalRoute.LastSeenFeedVersionId</c> is a bare column with no navigation property —
    /// the relationship is not configured because <c>FeedManager</c> maintains it through raw SQL —
    /// so this cannot be an <c>Include</c>.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, string>> GetFeedSlugsAsync(IEnumerable<int?> feedVersionIds, CancellationToken ct)
    {
        var ids = feedVersionIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];

        // fv.Feed.FeedId is the slug ("zet-2"); fv.FeedId is the int FK. Not interchangeable.
        return await _db.FeedVersions.AsNoTracking()
            .Where(fv => ids.Contains(fv.Id))
            .Select(fv => new { fv.Id, Slug = fv.Feed.FeedId })
            .ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
    }

    public async Task<List<RouteResponse>> GetAllAsync(int? operatorId, RouteType? routeType, string? q, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable().AsNoTracking();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.LongName.Contains(q) || r.ShortName.Contains(q));

        var rows = await query.OrderBy(r => r.Id).Skip((page - 1) * perPage).Take(perPage)
            .Select(r => new
            {
                Response = new RouteResponse
                {
                    Id = r.Id,
                    OnestopId = r.OnestopId,
                    Name = r.LongName,
                    ShortName = r.ShortName,
                    RouteType = r.RouteType.ToString(),
                    OperatorId = r.OperatorId,
                    OperatorName = r.Operator != null ? r.Operator.Name : null,
                    OperatorGlobalId = r.Operator != null ? r.Operator.GlobalId : null
                },
                r.LastSeenFeedVersionId
            })
            .ToListAsync(ct);

        var slugs = await GetFeedSlugsAsync(rows.Select(x => x.LastSeenFeedVersionId), ct);
        foreach (var row in rows)
            row.Response.FeedId = row.LastSeenFeedVersionId is int fv && slugs.TryGetValue(fv, out var slug) ? slug : null;

        return rows.Select(x => x.Response).ToList();
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
        IQueryable<CanonicalRoute> query = _db.CanonicalRoutes.AsNoTracking().Where(r => r.IsActive);
        query = query.Include(r => r.Operator);

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

        var slugs = await GetFeedSlugsAsync(routes.Select(r => r.LastSeenFeedVersionId), ct);

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
                ["operatorGlobalId"] = r.Operator?.GlobalId,
                ["feedId"] = r.LastSeenFeedVersionId is int fv && slugs.TryGetValue(fv, out var slug) ? slug : null,
                ["shapeEdited"] = r.ShapeEdited
            });
    }

    public async Task<RouteResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        var row = await _db.CanonicalRoutes
            .Where(r => r.Id == id)
            .Select(r => new
            {
                Response = new RouteResponse
                {
                    Id = r.Id,
                    OnestopId = r.OnestopId,
                    Name = r.LongName,
                    ShortName = r.ShortName,
                    RouteType = r.RouteType.ToString(),
                    OperatorId = r.OperatorId,
                    OperatorName = r.Operator != null ? r.Operator.Name : null,
                    OperatorGlobalId = r.Operator != null ? r.Operator.GlobalId : null
                },
                r.LastSeenFeedVersionId
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var slugs = await GetFeedSlugsAsync([row.LastSeenFeedVersionId], ct);
        row.Response.FeedId = row.LastSeenFeedVersionId is int fv && slugs.TryGetValue(fv, out var slug) ? slug : null;

        return row.Response;
    }

    public async Task<RouteResponse?> GetByOnestopIdAsync(string onestopId, CancellationToken ct)
    {
        var row = await _db.CanonicalRoutes
            .Where(r => r.OnestopId == onestopId)
            .Select(r => new
            {
                Response = new RouteResponse
                {
                    Id = r.Id,
                    OnestopId = r.OnestopId,
                    Name = r.LongName,
                    ShortName = r.ShortName,
                    RouteType = r.RouteType.ToString(),
                    OperatorId = r.OperatorId,
                    OperatorName = r.Operator != null ? r.Operator.Name : null,
                    OperatorGlobalId = r.Operator != null ? r.Operator.GlobalId : null
                },
                r.LastSeenFeedVersionId
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var slugs = await GetFeedSlugsAsync([row.LastSeenFeedVersionId], ct);
        row.Response.FeedId = row.LastSeenFeedVersionId is int fv && slugs.TryGetValue(fv, out var slug) ? slug : null;

        return row.Response;
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
        // Distinguishable failures. All three used to return null, which the controller renders as
        // 404 — so "this route has no editable shape" and "your geometry has one point" both came
        // back as "route not found", and the caller had no way to tell which it was.
        var route = await _db.CanonicalRoutes.FindAsync([id], ct)
            ?? throw new AppException($"Route {id} not found.", 404, "ROUTE_NOT_FOUND");

        var shape = await GetActiveShapeForRouteAsync(id, ct)
            ?? throw new AppException(
                $"Route {id} has no shape on an active feed version, so there is nothing to edit.",
                409, "ROUTE_HAS_NO_SHAPE");

        var coords = body.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
        if (coords.Length < 2)
            throw new AppException("A route shape needs at least two coordinates.", 400, "SHAPE_TOO_SHORT");

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
