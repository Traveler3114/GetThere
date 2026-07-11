using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class PlaceManager
{
    private readonly TransitDbContext _db;

    public PlaceManager(TransitDbContext db) { _db = db; }

    public async Task<List<PlaceResponse>> GetAllAsync(string? countryCode, int page, int perPage, CancellationToken ct = default)
    {
        var query = _db.Places.OrderBy(p => p.Id).AsQueryable();

        if (!string.IsNullOrEmpty(countryCode))
            query = query.Where(p => p.AdmCountryCode == countryCode);

        return await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(PlaceMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(string? countryCode, CancellationToken ct = default)
    {
        var query = _db.Places.AsQueryable();

        if (!string.IsNullOrEmpty(countryCode))
            query = query.Where(p => p.AdmCountryCode == countryCode);

        return await query.CountAsync(ct);
    }

    public async Task<PlaceResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Places
            .Where(p => p.Id == id)
            .Select(PlaceMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<OperatorResponse>> GetOperatorsAsync(int placeId, CancellationToken ct = default)
    {
        var operatorIds = await _db.CanonicalStations
            .Where(cs => cs.PlaceId == placeId)
            .SelectMany(cs => cs.StationOperators)
            .Select(cso => cso.OperatorId)
            .Distinct()
            .ToListAsync(ct);

        return await _db.Operators
            .Where(o => operatorIds.Contains(o.Id))
            .Select(OperatorMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<List<StationResponse>> GetStationsAsync(int placeId, CancellationToken ct = default)
    {
        return await _db.CanonicalStations
            .Where(cs => cs.PlaceId == placeId)
            .Select(StationMapper.ToResponseExpression)
            .ToListAsync(ct);
    }
}