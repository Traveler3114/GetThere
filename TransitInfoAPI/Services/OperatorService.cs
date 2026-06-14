using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Services;

public class OperatorService
{
    private readonly TransitDbContext _db;

    public OperatorService(TransitDbContext db)
    {
        _db = db;
    }

    public async Task<List<OperatorDto>> GetAllAsync(int? countryId, OperatorType? type, CancellationToken ct)
    {
        var query = _db.Operators.Include(o => o.Country).AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);
        if (type.HasValue)
            query = query.Where(o => o.OperatorType == type.Value);

        return await query.Select(o => new OperatorDto
        {
            Id = o.Id,
            GlobalId = o.GlobalId,
            Name = o.Name,
            ShortName = o.ShortName,
            Website = o.Website,
            OperatorType = o.OperatorType.ToString(),
            IsVerified = o.IsVerified,
            CountryName = o.Country.Name
        }).ToListAsync(ct);
    }

    public async Task<OperatorDto?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.Operators
            .Include(o => o.Country)
            .Where(o => o.GlobalId == globalId)
            .Select(o => new OperatorDto
            {
                Id = o.Id,
                GlobalId = o.GlobalId,
                Name = o.Name,
                ShortName = o.ShortName,
                Website = o.Website,
                OperatorType = o.OperatorType.ToString(),
                IsVerified = o.IsVerified,
                CountryName = o.Country.Name
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<StationDto>> GetStationsAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return [];

        return await _db.CanonicalStationOperators
            .Include(cso => cso.CanonicalStation).ThenInclude(cs => cs.Country)
            .Where(cso => cso.OperatorId == op.Id)
            .Select(cso => new StationDto
            {
                Id = cso.CanonicalStation.Id,
                GlobalId = cso.CanonicalStation.GlobalId,
                Name = cso.CanonicalStation.Name,
                Latitude = cso.CanonicalStation.Latitude,
                Longitude = cso.CanonicalStation.Longitude,
                StationType = cso.CanonicalStation.StationType.ToString(),
                CountryName = cso.CanonicalStation.Country.Name
            })
            .ToListAsync(ct);
    }

    public async Task<List<RouteDto>> GetRoutesAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return [];

        return await _db.CanonicalRoutes
            .Where(r => r.OperatorId == op.Id && r.IsActive)
            .Select(r => new RouteDto
            {
                Id = r.Id,
                GlobalId = r.GlobalId,
                Name = r.LongName,
                ShortName = r.ShortName,
                RouteType = r.RouteType.ToString(),
                OperatorId = r.OperatorId
            })
            .ToListAsync(ct);
    }

    public async Task<List<FeedDto>> GetFeedsAsync(string globalId, CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == globalId, ct);
        if (op is null) return [];

        return await _db.Feeds
            .Where(f => f.OperatorId == op.Id && f.IsActive)
            .Select(f => new FeedDto
            {
                Id = f.Id,
                FeedType = f.FeedType.ToString(),
                FeedId = f.FeedId,
                ExternalUrl = f.ExternalUrl,
                InternalUrl = f.InternalUrl,
                IsActive = f.IsActive,
                LastFetched = f.LastFetched,
                RefreshIntervalSeconds = f.RefreshIntervalSeconds
            })
            .ToListAsync(ct);
    }
}
