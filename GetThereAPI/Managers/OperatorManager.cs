using GetThereAPI.Data;
using GetThereAPI.Transit;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class OperatorManager
{
    private readonly AppDbContext _db;
    private readonly MobilityManager _mobility;
    private readonly TransitOrchestrator _transit;

    public OperatorManager(
        AppDbContext db,
        MobilityManager mobility,
        TransitOrchestrator transit)
    {
        _db = db;
        _mobility = mobility;
        _transit = transit;
    }

    private static readonly Dictionary<int, int> MobilityProviderIds = new()
    {
        [3] = 1,
    };

    private static readonly Dictionary<int, int> TicketableToDbTransitId = new()
    {
        [1] = 1,
        [2] = 2,
        [4] = 3,
    };

    private static string BuildOtpFeedId(int operatorId) => $"op{operatorId}";

    private static readonly List<TicketableOperatorDto> TicketableList =
    [
        new TicketableOperatorDto
        {
            Id = 1, Name = "ZET", Type = "TRANSIT", Color = "#1264AB",
            Description = "Zagreb's tram and bus network.",
            City = "Zagreb", Country = "Croatia", IsMock = true,
        },
        new TicketableOperatorDto
        {
            Id = 2, Name = "HZPP", Type = "TRAIN", Color = "#6a1b9a",
            Description = "Croatian national railway — trains across Croatia.",
            City = "Zagreb", Country = "Croatia", IsMock = true,
        },
        new TicketableOperatorDto
        {
            Id = 3, Name = "Bajs", Type = "BIKE", Color = "#FF6B00",
            Description = "Nextbike city bike sharing service.",
            City = "", Country = "", IsMock = true,
        },
        new TicketableOperatorDto
        {
            Id = 4, Name = "LPP", Type = "TRANSIT", Color = "#E30613",
            Description = "Ljubljana's city bus network.",
            City = "Ljubljana", Country = "Slovenia", IsMock = true,
        },
    ];

    public async Task<List<TicketableOperatorDto>> GetTicketableOperatorsAsync(int? countryId)
    {
        var dbOps = await _db.TransitOperators
            .Where(o => TicketableToDbTransitId.Values.Contains(o.Id))
            .Select(o => new { o.Id, o.LogoUrl })
            .ToListAsync();

        var logoMap = dbOps.ToDictionary(o => o.Id, o => o.LogoUrl);

        string? countryName = null;
        if (countryId.HasValue)
        {
            countryName = await _db.Countries
                .Where(c => c.Id == countryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            if (countryName is null)
                return [];
        }

        var result = new List<TicketableOperatorDto>();

        foreach (var t in TicketableList)
        {
            if (MobilityProviderIds.TryGetValue(t.Id, out var mobilityDbId))
            {
                if (countryName is not null
                    && !_mobility.HasStationsInCountry(mobilityDbId, countryName))
                {
                    continue;
                }

                result.Add(new TicketableOperatorDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Type = t.Type,
                    Color = t.Color,
                    Description = t.Description,
                    City = t.City,
                    Country = countryName ?? t.Country,
                    IsMock = t.IsMock,
                    LogoUrl = t.LogoUrl,
                });
                continue;
            }

            if (countryName is not null && t.Country != countryName)
                continue;

            TicketableToDbTransitId.TryGetValue(t.Id, out var dbId);
            result.Add(new TicketableOperatorDto
            {
                Id = t.Id,
                Name = t.Name,
                Type = t.Type,
                Color = t.Color,
                Description = t.Description,
                City = t.City,
                Country = t.Country,
                IsMock = t.IsMock,
                LogoUrl = logoMap.TryGetValue(dbId, out var url) ? url : t.LogoUrl,
            });
        }

        return result;
    }

    public async Task<List<TransportTypeDto>> GetTransportTypesAsync(IWebHostEnvironment env)
    {
        var imagesPath = Path.Combine(env.WebRootPath, "images");

        var all = await _db.TransportTypes
            .Select(t => new TransportTypeDto
            {
                GtfsRouteType = t.GtfsRouteType,
                Name = t.Name,
                IconFile = t.IconFile,
                Color = t.Color,
            })
            .ToListAsync();

        return all.Where(t => File.Exists(Path.Combine(imagesPath, t.IconFile))).ToList();
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

    /// <summary>
    /// Returns stops from OTP, filtered by country if specified.
    /// Filtering works by getting the feed IDs for operators in the requested country,
    /// then keeping only stops whose stopId starts with one of those feed prefixes.
    /// OTP prefixes every stop ID with its feedId (e.g. "zet:stop123", "hzpp:stop456").
    /// </summary>
    public async Task<List<StopDto>> GetAllStopsAsync(int? countryId = null, CancellationToken ct = default)
    {
        var stops = await _transit.GetStopsAsync(countryId, ct);

        if (!countryId.HasValue)
            return stops;

        // Get the OTP feed ID prefixes for all operators in this country
        var feedPrefixes = await GetFeedPrefixesForCountryAsync(countryId.Value, ct);

        if (feedPrefixes.Count == 0)
            return stops; // No operators found for this country — return all rather than nothing

        return stops
            .Where(s => feedPrefixes.Any(prefix =>
                s.StopId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Returns routes from OTP, filtered by country if specified.
    /// Same prefix-based filtering as stops.
    /// </summary>
    public async Task<List<RouteDto>> GetAllRoutesAsync(int? countryId = null, CancellationToken ct = default)
    {
        var routes = await _transit.GetRoutesAsync(countryId, ct);

        if (!countryId.HasValue)
            return routes;

        var feedPrefixes = await GetFeedPrefixesForCountryAsync(countryId.Value, ct);

        if (feedPrefixes.Count == 0)
            return routes;

        return routes
            .Where(r => feedPrefixes.Any(prefix =>
                r.RouteId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public Task<StopScheduleDto?> GetStopScheduleAsync(
        string stopId,
        int? countryId = null,
        CancellationToken ct = default)
        => _transit.GetStopScheduleAsync(countryId, stopId, ct);

    public Task<bool> IsTransitHealthyAsync(int? countryId = null, CancellationToken ct = default)
        => _transit.HealthCheckAsync(countryId, ct);

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

    /// <summary>
    /// Gets the OTP feed ID prefixes (e.g. "zet:", "hzpp:") for all operators
    /// in a given country. OTP prefixes stop/route IDs with "{feedId}:" so we
    /// can use these to filter results returned from the single shared OTP instance.
    /// </summary>
    private async Task<List<string>> GetFeedPrefixesForCountryAsync(int countryId, CancellationToken ct)
    {
        var operatorIds = await _db.TransitOperators
            .Where(o => o.CountryId == countryId && o.GtfsFeedUrl != null)
            .Select(o => o.Id)
            .ToListAsync(ct);

        // OTP feed IDs are built as the GTFS feedId set in build-config.json.
        // In build-config.json generated by DbBackedOtpConfigLoader, feedId = "op{operatorId}".
        // However the actual OTP GTFS feedId from the transit agency files (e.g. "zet", "hzpp", "lpp")
        // is what OTP uses to prefix stop IDs. We need to match the feedId passed to OTP.
        //
        // The build-config.json uses feedId = BuildOtpFeedId(operatorId) = "op{id}",
        // so OTP will prefix stops as "op1:stopId", "op2:stopId", etc.
        return operatorIds
            .Select(id => $"{BuildOtpFeedId(id)}:")
            .ToList();
    }
}