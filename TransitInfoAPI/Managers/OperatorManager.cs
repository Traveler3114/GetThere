using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class OperatorManager
{
    private readonly TransitDbContext _db;

    public OperatorManager(TransitDbContext db)
    {
        _db = db;
    }

    public async Task<List<OperatorResponse>> GetAllAsync(int? countryId, string? q, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.Operators.Include(o => o.Country).AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(o => o.Name.Contains(q) || o.ShortName.Contains(q));

        return await query.OrderBy(o => o.Id).Skip((page - 1) * perPage).Take(perPage).Select(OperatorMapper.ToResponseExpression).ToListAsync(ct);
    }

    public async Task<OperatorResponse?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.Operators
            .Include(o => o.Country)
            .Where(o => o.GlobalId == globalId)
            .Select(OperatorMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
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
