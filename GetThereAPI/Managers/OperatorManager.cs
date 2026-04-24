using GetThereAPI.Data;
using GetThereAPI.Infrastructure;
using GetThereShared.Dtos;
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

    public async Task<List<TransportTypeDto>> GetTransportTypesAsync()
    {
        var all = await _db.TransportTypes
            .Select(t => new TransportTypeDto
            {
                GtfsRouteType = t.GtfsRouteType,
                Name = t.Name,
                IconFile = t.IconFile,
                Color = t.Color,
            })
            .ToListAsync();

        return all.Where(t => _iconFileStore.Exists(t.IconFile)).ToList();
    }

    public async Task<List<OperatorDto>> GetAllOperatorsAsync(int? countryId = null)
    {
        var query = _db.TransitOperators
            .Include(o => o.Country)
            .Include(o => o.City)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);

        return await query
            .OrderBy(o => o.Name)
            .Select(o => new OperatorDto
            {
                Id = o.Id,
                Name = o.Name,
                LogoUrl = o.LogoUrl,
                City = o.City != null ? o.City.Name : null,
                Country = o.Country.Name,
            })
            .ToListAsync();
    }

    public async Task<List<BikeStationDto>> GetBikeStationsAsync(int? countryId)
    {
        var countryName = await GetCountryNameAsync(countryId);
        if (countryId.HasValue && countryName is null)
            return [];

        return _mobility.GetAllStations(countryName);
    }

    public async Task<List<OtpOperatorFeedDto>> GetOtpFeedOperatorsAsync()
    {
        return await _db.TransitOperators
            .Include(o => o.Country)
            .OrderBy(o => o.Country.Name)
            .ThenBy(o => o.Name)
            .Select(o => new OtpOperatorFeedDto
            {
                OperatorId = o.Id,
                OperatorName = o.Name,
                CountryId = o.CountryId,
                CountryName = o.Country.Name,
                FeedId = BuildOtpFeedId(o.Id),
                StaticGtfsUrl = o.GtfsFeedUrl,
                GtfsRealtimeUrl = o.GtfsRealtimeFeedUrl
            })
            .ToListAsync();
    }

    private async Task<string?> GetCountryNameAsync(int? countryId)
    {
        if (!countryId.HasValue)
            return null;

        return await _db.Countries
            .Where(c => c.Id == countryId.Value)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();
    }
}
