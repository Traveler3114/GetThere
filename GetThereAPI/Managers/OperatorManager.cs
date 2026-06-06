using GetThereAPI.Data;
using GetThereAPI.Infrastructure;
using GetThereShared.Contracts;
using Microsoft.EntityFrameworkCore;

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

    private static string BuildOtpFeedId(int operatorId) => $"op{operatorId}";

    public async Task<List<TransportTypeResponse>> GetTransportTypesAsync(CancellationToken ct = default)
    {
        var all = await _db.TransportTypes
            .Select(t => new TransportTypeResponse
            {
                GtfsRouteType = t.GtfsRouteType,
                Name = t.Name,
                IconFile = t.IconFile,
                Color = t.Color,
            })
            .ToListAsync(ct);

        return all.Where(t => _iconFileStore.Exists(t.IconFile)).ToList();
    }

    public async Task<List<OperatorResponse>> GetAllOperatorsAsync(int? countryId = null, CancellationToken ct = default)
    {
        var query = _db.TransitOperators
            .Include(o => o.Country)
            .Include(o => o.City)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);

        return await query
            .OrderBy(o => o.Name)
            .Select(o => new OperatorResponse
            {
                Id = o.Id,
                Name = o.Name,
                LogoUrl = o.LogoUrl,
                City = o.City != null ? o.City.Name : null,
                Country = o.Country.Name,
            })
            .ToListAsync(ct);
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
        return await _db.TransitOperators
            .Include(o => o.Country)
            .OrderBy(o => o.Country.Name)
            .ThenBy(o => o.Name)
            .Select(o => new OperatorFeedResponse
            {
                OperatorId = o.Id,
                OperatorName = o.Name,
                CountryId = o.CountryId,
                CountryName = o.Country.Name,
                FeedId = BuildOtpFeedId(o.Id),
                StaticGtfsUrl = o.GtfsFeedUrl,
                GtfsRealtimeUrl = o.GtfsRealtimeFeedUrl
            })
            .ToListAsync(ct);
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
