using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Managers;

public class OperatorManager
{
    private readonly TransitDbContext _db;
    private readonly OnestopIdManager _onestopId;

    public OperatorManager(TransitDbContext db, OnestopIdManager onestopId) { _db = db; _onestopId = onestopId; }

    public async Task<List<OperatorResponse>> GetAllAsync(string? q, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.Operators.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(o => o.Name.Contains(q) || o.ShortName.Contains(q));

        return await query.OrderBy(o => o.Id).Skip((page - 1) * perPage).Take(perPage).Select(OperatorMapper.ToResponseExpression).ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(string? q, CancellationToken ct = default)
    {
        var query = _db.Operators.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(o => o.Name.Contains(q) || o.ShortName.Contains(q));

        return await query.CountAsync(ct);
    }

    public async Task<OperatorResponse?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.Operators
            .Where(o => o.GlobalId == globalId)
            .Select(OperatorMapper.ToResponseExpression)
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
        var operators = await _db.Operators.OrderBy(o => o.Id).Skip((page - 1) * perPage).Take(perPage)
            .Select(o => new
            {
                Operator = o,
                StationLat = o.StationOperators
                    .Select(cso => (double?)cso.CanonicalStation.Latitude)
                    .FirstOrDefault(),
                StationLon = o.StationOperators
                    .Select(cso => (double?)cso.CanonicalStation.Longitude)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var fc = new GeoJsonFeatureCollection
        {
            Features = operators.Select(item => new GeoJsonFeature
            {
                Geometry = item.StationLat.HasValue && item.StationLon.HasValue
                    ? new { type = "Point", coordinates = new[] { item.StationLon.Value, item.StationLat.Value } }
                    : null,
                Properties = new Dictionary<string, object?>
                {
                    ["id"] = item.Operator.Id,
                    ["globalId"] = item.Operator.GlobalId,
                    ["onestopId"] = item.Operator.OnestopId,
                    ["name"] = item.Operator.Name,
                    ["shortName"] = item.Operator.ShortName
                }
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

        return new
        {
            type = "Feature",
            geometry = new
            {
                type = geom.GeometryType,
                coordinates = geom.Coordinates.Select(c => new[] { c.X, c.Y })
            },
            properties = new { }
        };
    }

    public async Task<List<object>> GetTypesAsync()
    {
        var icons = new Dictionary<int, (string Icon, string Color)>
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
                icons.TryGetValue(id, out var meta);
                return new { Id = id, Name = name, IconFile = meta.Icon ?? $"{rt.ToString().ToLower()}.png", Color = meta.Color ?? "#808080" };
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
        await _db.SaveChangesAsync(ct);

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
        var op = await _db.Operators
            .Include(o => o.Agencies)
            .Include(o => o.Feeds)
            .Include(o => o.Routes)
            .Include(o => o.StationOperators)
            .FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);

        if (op is null) return false;

        var totalAssociations = op.Agencies.Count + op.Feeds.Count + op.Routes.Count + op.StationOperators.Count;
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

        return await _db.CanonicalStationOperators
            .Include(cso => cso.CanonicalStation).ThenInclude(cs => cs.Country)
            .Where(cso => cso.OperatorId == op.Id)
            .Select(cso => cso.CanonicalStation)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<List<RouteResponse>> GetRoutesAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return [];

        return await _db.CanonicalRoutes
            .Where(r => r.OperatorId == op.Id && r.IsActive)
            .Take(500)
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
