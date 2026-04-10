using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// Handles all transit-related business logic.
///
/// Stops + routes + schedules  → Transitland REST API
/// </summary>
public class OperatorManager
{
    private readonly AppDbContext _db;
    private readonly TransitlandManager _transitland;
    private readonly ILogger<OperatorManager> _logger;

    public OperatorManager(
        AppDbContext db,
        TransitlandManager transitland,
        ILogger<OperatorManager> logger)
    {
        _db = db;
        _transitland = transitland;
        _logger = logger;
    }

    // ── Hardcoded ticketable operator definitions ─────────────────────────

    private static readonly Dictionary<int, int> MobilityProviderIds = new()
    {
        [3] = 1,  // Bajs (ticketable Id=3) → MobilityProvider DB Id=1
    };

    private static readonly Dictionary<int, int> TicketableToDbTransitId = new()
    {
        [1] = 1,   // ZET  → TransitOperator.Id = 1
        [2] = 2,   // HZPP → TransitOperator.Id = 2
        [4] = 3,   // LPP  → TransitOperator.Id = 3
    };

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
            if (MobilityProviderIds.ContainsKey(t.Id))
            {
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

    // ── Transport types ───────────────────────────────────────────────────

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

    // ── Operators ─────────────────────────────────────────────────────────

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

    // ── Stops — now served from Transitland ──────────────────────────────

    /// <summary>
    /// Returns all stops from Transitland, optionally filtered by country.
    /// This replaces the old local GTFS parser — no ZIP files needed.
    /// </summary>
    public async Task<List<StopDto>> GetAllStopsAsync(
        int? countryId = null,
        CancellationToken cancellationToken = default)
    {
        var countryName = await ResolveCountryNameAsync(countryId, cancellationToken);

        // If a countryId was given but we can't find it, return empty
        if (countryId.HasValue && countryName is null)
            return [];

        return await _transitland.GetStopsAsync(countryName, cancellationToken);
    }

    // ── Routes — no longer available without local GTFS data ─────────────

    /// <summary>
    /// Returns an empty list — local GTFS parsing has been removed.
    /// All stop/route data is now served via the Transitland API.
    /// </summary>
    public List<RouteDto> GetAllRoutes(int? countryId = null) => [];

    // ── Vehicles — no longer available without local GTFS-RT ─────────────

    /// <summary>
    /// Returns an empty list — local GTFS-RT parsing has been removed.
    /// </summary>
    public List<VehicleDto> GetAllVehicles(int? countryId = null) => [];

    // ── Stop schedule — now served from Transitland ───────────────────────

    /// <summary>
    /// Returns today's departures for a stop with realtime delays merged in.
    /// Transitland already merges scheduled + realtime, so we just return its response.
    /// </summary>
    public async Task<StopScheduleDto?> GetStopScheduleAsync(
        string stopId,
        CancellationToken cancellationToken = default)
    {
        var detailsTask = _transitland.GetStopDetailsAsync(stopId, cancellationToken);
        var departuresTask = _transitland.GetStopDeparturesAsync(stopId, limit: 10, cancellationToken);

        await Task.WhenAll(detailsTask, departuresTask);

        var details = detailsTask.Result;
        var stopTimes = departuresTask.Result;

        var groups = stopTimes
            .GroupBy(st => (st.RouteId, st.Headsign))
            .Select(g =>
            {
                var first = g.First();
                return new DepartureGroupDto
                {
                    RouteId = g.Key.RouteId,
                    ShortName = first.RouteShortName,
                    Headsign = g.Key.Headsign,
                    Departures = g.Select(st => new DepartureDto
                    {
                        TripId = st.TripId,
                        ScheduledTime = st.ScheduledTime,
                        EstimatedTime = st.EstimatedTime,
                        DelayMinutes = st.DelayMinutes,
                        IsRealtime = st.IsRealtime,
                    }).ToList(),
                };
            })
            .ToList();

        return new StopScheduleDto
        {
            StopId = stopId,
            StopName = details?.Name ?? stopId,
            Groups = groups,
        };
    }

    // ── Trip detail — not available without local GTFS data ──────────────

    /// <summary>
    /// Trip detail lookup requires local GTFS data which has been removed.
    /// Returns null for all requests.
    /// </summary>
    public Task<TripDetailDto?> GetTripDetailAsync(string tripId)
    {
        _logger.LogWarning("[Trip] Trip detail lookup is unavailable — local GTFS parsing has been removed");
        return Task.FromResult<TripDetailDto?>(null);
    }

    // ── Startup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called once on server startup.
    /// Stops + schedules now come from Transitland — no local GTFS loading needed.
    /// </summary>
    public Task InitialiseAsync()
    {
        _logger.LogInformation("[Startup] OperatorManager initialised (Transitland-only mode)");
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<string?> ResolveCountryNameAsync(
        int? countryId,
        CancellationToken cancellationToken = default)
    {
        if (!countryId.HasValue) return null;

        return await _db.Countries
            .Where(c => c.Id == countryId.Value)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }
}