using Microsoft.EntityFrameworkCore;

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

public class OperatorManager
{
    private readonly TransitDbContext _db;
    private readonly OnestopIdManager _onestopId;

    public OperatorManager(TransitDbContext db, OnestopIdManager onestopId) { _db = db; _onestopId = onestopId; }

    /// <summary>Ceiling on the un-paged association lists below.</summary>
    private const int MaxAssociatedRecords = 500;

    public async Task<List<OperatorResponse>> GetAllAsync(string? q, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.Operators.AsQueryable().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(o => o.Name.Contains(q) || o.ShortName.Contains(q));

        return await query.OrderBy(o => o.Id).Skip((page - 1) * perPage).Take(perPage).Select(OperatorMapper.ToResponseExpression).ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(string? q, CancellationToken ct = default)
    {
        var query = _db.Operators.AsQueryable().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(o => o.Name.Contains(q) || o.ShortName.Contains(q));

        return await query.CountAsync(ct);
    }

    public async Task<OperatorResponse?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.Operators
            .Where(o => o.GlobalId == globalId)
            .Select(OperatorMapper.ToResponseExpression)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OperatorResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.Operators
            .Where(o => o.Id == id)
            .Select(OperatorMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OperatorResponse?> GetByOnestopIdAsync(string onestopId, CancellationToken ct)
    {
        return await _db.Operators
            .Where(o => o.OnestopId == onestopId)
            .Select(OperatorMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<object> GetAllGeoJsonAsync(int page, int perPage, CancellationToken ct)
    {
        // One ordered subquery for the whole coordinate, not one per axis.
        //
        // The latitude and the longitude used to be two independent FirstOrDefault subqueries over
        // an unordered set, so nothing tied them to the same station: for an operator serving more
        // than one station, SQL Server is free to answer the two subqueries from different rows and
        // the pin lands at a coordinate that is neither of them. Ordering also makes the choice
        // stable — without it the pin could move between two identical requests.
        var operators = await _db.Operators.OrderBy(o => o.Id).Skip((page - 1) * perPage).Take(perPage)
            .Select(o => new
            {
                Operator = o,
                Station = o.StationOperators
                    .OrderBy(cso => cso.CanonicalStationId)
                    .Select(cso => new { cso.CanonicalStation.Latitude, cso.CanonicalStation.Longitude })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var fc = new GeoJsonFeatureCollection
        {
            Features = operators.Select(item =>
            {
                var station = item.Station;
                return new GeoJsonFeature
                {
                    Geometry = station is not null
                        ? new { type = "Point", coordinates = new[] { station.Longitude, station.Latitude } }
                        : null,
                    Properties = new Dictionary<string, object?>
                    {
                        ["id"] = item.Operator.Id,
                        ["globalId"] = item.Operator.GlobalId,
                        ["onestopId"] = item.Operator.OnestopId,
                        ["name"] = item.Operator.Name,
                        ["shortName"] = item.Operator.ShortName
                    }
                };
            }).ToList()
        };
        return fc;
    }

    public async Task<object> GetServiceAreaAsync(int id, CancellationToken ct)
    {
        var hull = await _db.FeedVersions
            .Where(fv => fv.ConvexHull != null && fv.Agencies.Any(a => a.OperatorId == id))
            .OrderByDescending(fv => fv.ImportedAt)
            .Select(fv => fv.ConvexHull)
            .FirstOrDefaultAsync(ct);

        Geometry geom;
        if (hull is not null)
        {
            geom = hull;
        }
        else
        {
            var coords = await _db.CanonicalStationOperators
                .Where(cso => cso.OperatorId == id)
                .Select(cso => cso.CanonicalStation)
                .Where(cs => cs != null && cs.IsActive)
                .Select(cs => new Coordinate(cs.Longitude, cs.Latitude))
                .ToListAsync(ct);

            if (coords.Count == 0)
                return new { type = "Feature", geometry = (object?)null, properties = new { } };

            var computed = new ConvexHull(coords.ToArray(), GeometryFactory.Default).GetConvexHull();
            if (computed is Polygon polygon && !Orientation.IsCCW(polygon.Shell.Coordinates))
                computed = polygon.Reverse();
            geom = computed;
        }

        // Built through the shared converter rather than by hand. This projected
        // `geom.Coordinates.Select(...)`, which flattens *any* geometry to a bare point list — but a
        // convex hull is a Polygon, whose GeoJSON coordinates must be an array of linear rings. The
        // endpoint therefore emitted {"type":"Polygon","coordinates":[[x,y],…]}: one nesting level
        // short, invalid, and unrenderable by any client. FromNtsGeometry handles every geometry
        // type this can produce.
        return new
        {
            type = "Feature",
            geometry = GeoJsonGeometry.FromNtsGeometry(geom),
            properties = new { }
        };
    }

    /// <summary>
    /// Icon and legend colour per GTFS route type, for the map's legend and its markers.
    /// <para>
    /// Static because it never varies and <c>GetTypesAsync</c> is called on every map load; it was
    /// being rebuilt, thirteen entries at a time, per request.
    /// </para>
    /// </summary>
    private static readonly Dictionary<int, (string Icon, string Color)> RouteTypeIcons = new()
    {
        { 0, ("tram.png", "#126400") },
        { 1, ("subway.png", "#e31a1c") },
        { 2, ("train.png", "#b15928") },
        { 3, ("bus.png", "#1f78b4") },
        { 4, ("ferry.png", "#6a3d9a") },
        { 5, ("cabletram.png", "#fb9a99") },
        { 6, ("cablecar.png", "#fb9a99") },
        { 7, ("funicular.png", "#fdbf6f") },
        { 11, ("trolleybus.png", "#33a02c") },
        { 12, ("monorail.png", "#cab2d6") },
        { 100, ("bicycle.png", "#a6cee3") },
        { 101, ("scooter.png", "#ff7f00") },
        { 200, ("airplane.png", "#b2df8a") }
    };

    /// <summary>Fallback marker colour for a route type with no entry in <see cref="RouteTypeIcons"/>.</summary>
    private const string DefaultRouteTypeColor = "#808080";

    public async Task<List<object>> GetTypesAsync()
    {
        return Enum.GetValues<RouteType>()
            .Select(rt =>
            {
                var id = (int)rt;
                var name = rt switch
                {
                    RouteType.Tram => "Tram",
                    RouteType.Subway => "Subway",
                    RouteType.Train => "Train",
                    RouteType.Bus => "Bus",
                    RouteType.Ferry => "Ferry",
                    RouteType.CableTram => "Cable Tram",
                    RouteType.CableCar => "Cable Car",
                    RouteType.Funicular => "Funicular",
                    RouteType.Trolleybus => "Trolleybus",
                    RouteType.Monorail => "Monorail",
                    RouteType.Bicycle => "Bicycle",
                    RouteType.Scooter => "Scooter",
                    RouteType.Airplane => "Airplane",
                    _ => rt.ToString()
                };
                RouteTypeIcons.TryGetValue(id, out var meta);
                return new { Id = id, Name = name, IconFile = meta.Icon ?? $"{rt.ToString().ToLowerInvariant()}.png", Color = meta.Color ?? DefaultRouteTypeColor };
            })
            .ToList<object>();
    }

    public async Task<OperatorResponse> CreateAsync(CreateOperatorRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new AppException("Operator name is required.", 400);

        if (string.IsNullOrWhiteSpace(request.ShortName))
            throw new AppException("Short name is required.", 400);

        var globalId = request.GlobalId;
        if (string.IsNullOrWhiteSpace(globalId))
            globalId = $"gt-{request.ShortName.ToLowerInvariant()}";

        var exists = await _db.Operators.AnyAsync(o => o.GlobalId == globalId, ct);
        if (exists)
            throw new AppException($"Operator with GlobalId '{globalId}' already exists.", 409);

        var onestopId = _onestopId.GenerateOperatorOnestopId(request.ShortName);
        var onestopExists = await _db.Operators.AnyAsync(o => o.OnestopId == onestopId, ct);
        if (onestopExists)
            throw new AppException($"Operator with OnestopId '{onestopId}' already exists.", 409);

        var op = new Operator
        {
            GlobalId = globalId,
            OnestopId = onestopId,
            Name = request.Name,
            ShortName = request.ShortName,
            Website = request.Website,
            CreatedAt = DateTime.UtcNow
        };

        _db.Operators.Add(op);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // Both AnyAsync checks above are pre-checks. Feed imports run three at a time and create
            // operators, so the window between check and insert is genuinely reachable.
            throw new AppException($"Operator '{globalId}' already exists.", 409);
        }

        return OperatorMapper.ToResponse(op);
    }

    public async Task<bool> UpdateAsync(string globalId, UpdateOperatorRequest request, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return false;

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new AppException("Name cannot be empty.", 400);
            op.Name = request.Name;
        }

        if (request.ShortName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.ShortName))
                throw new AppException("ShortName cannot be empty.", 400);
            op.ShortName = request.ShortName;
        }

        if (request.Website is not null)
            op.Website = request.Website;

        await _db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> DeleteAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);

        if (op is null) return false;

        // Counted in the database rather than loaded. This used to Include all four collections and
        // count them in memory, so refusing to delete a busy operator first pulled every route and
        // every station association it has into the change tracker — thousands of entities
        // materialised to produce a number on the path that then throws them away.
        var totalAssociations =
            await _db.Agencies.CountAsync(a => a.OperatorId == op.Id, ct)
            + await _db.Feeds.CountAsync(f => f.OperatorId == op.Id, ct)
            + await _db.CanonicalRoutes.CountAsync(r => r.OperatorId == op.Id, ct)
            + await _db.CanonicalStationOperators.CountAsync(cso => cso.OperatorId == op.Id, ct);

        if (totalAssociations > 0)
            throw new AppException($"Cannot delete operator: has {totalAssociations} associated record(s). Remove associations first.", 409);

        _db.Operators.Remove(op);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<List<StationResponse>> GetStationsAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return [];

        // Capped like the sibling GetRoutesAsync. A large operator returned every station it serves
        // in one unbounded response.
        //
        // Ordered because the cap makes ordering load-bearing: TOP without ORDER BY lets SQL Server
        // return any 500 rows it likes, so an operator with more stations than the ceiling showed a
        // different arbitrary subset on each request, with no way to reach the rest.
        return await _db.CanonicalStationOperators
            .Where(cso => cso.OperatorId == op.Id)
            .OrderBy(cso => cso.CanonicalStationId)
            .Select(cso => cso.CanonicalStation)
            .Take(MaxAssociatedRecords)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<List<RouteResponse>> GetRoutesAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return [];

        return await _db.CanonicalRoutes
            .Where(r => r.OperatorId == op.Id && r.IsActive)
            .OrderBy(r => r.Id)
            .Take(MaxAssociatedRecords)
            .Select(RouteMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<List<FeedResponse>> GetFeedsAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return [];

        return await _db.Feeds
            .Where(f => f.OperatorId == op.Id && f.IsActive)
            .Select(FeedMapper.ToResponseExpression)
            .ToListAsync(ct);
    }
}
