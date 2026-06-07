using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Infrastructure;
using GetThereAPI.Mapping;
using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public class OperatorManager
{
    private readonly AppDbContext _db;
    private readonly IBikeStationCache _mobility;
    private readonly IIconFileStore _iconFileStore;

    public OperatorManager(
        AppDbContext db,
        IBikeStationCache mobility,
        IIconFileStore iconFileStore)
    {
        _db = db;
        _mobility = mobility;
        _iconFileStore = iconFileStore;
    }

    public async Task<List<TransportTypeResponse>> GetTransportTypesAsync(CancellationToken ct = default)
    {
        var all = await _db.TransportTypes
            .ToListAsync(ct);

        return all
            .Where(t => _iconFileStore.Exists(t.IconFile))
            .Select(OperatorMapper.ToResponse)
            .ToList();
    }

    public async Task<List<OperatorResponse>> GetAllOperatorsAsync(int? countryId = null, CancellationToken ct = default)
    {
        var query = _db.TransitOperators
            .Include(o => o.Country)
            .Include(o => o.City)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);

        var operators = await query
            .OrderBy(o => o.Name)
            .ToListAsync(ct);

        return operators.Select(OperatorMapper.ToResponse).ToList();
    }

    public async Task<List<BikeStationResponse>> GetBikeStationsAsync(int? countryId, CancellationToken ct = default)
    {
        var countryName = await GetCountryNameAsync(countryId, ct);
        if (countryId.HasValue && countryName is null)
            return [];

        return _mobility.GetAllStations(countryName);
    }

    public async Task<List<OperatorFeedResponse>> GetOtpFeedOperatorsAsync(CancellationToken ct = default)
    {
        var operators = await _db.TransitOperators
            .Include(o => o.Country)
            .OrderBy(o => o.Country.Name)
            .ThenBy(o => o.Name)
            .ToListAsync(ct);

        return operators.Select(OperatorMapper.ToFeedResponse).ToList();
    }

    private async Task<string?> GetCountryNameAsync(int? countryId, CancellationToken ct = default)
    {
        if (!countryId.HasValue)
            return null;

        return await _db.Countries
            .Where(c => c.Id == countryId.Value)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);
    }
}
