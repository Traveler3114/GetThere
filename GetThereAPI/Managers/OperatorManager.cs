using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// Handles all transit-related business logic.
///
/// Stops + routes + schedules  → Transitland REST API (replaces local GTFS parsing)
/// Realtime vehicle positions  → RealtimeManager (GTFS-RT, unchanged)
/// Bike stations               → MobilityManager (GBFS, unchanged)
/// </summary>
public class OperatorManager
{
    private readonly AppDbContext _db;
    private readonly StaticDataManager _staticData;
    private readonly RealtimeManager _realtime;
    private readonly MobilityManager _mobility;
    private readonly TransitlandManager _transitland;
    private readonly ILogger<OperatorManager> _logger;

    public OperatorManager(
        AppDbContext db,
        StaticDataManager staticData,
        RealtimeManager realtime,
        MobilityManager mobility,
        TransitlandManager transitland,
        ILogger<OperatorManager> logger)
    {
        _db = db;
        _staticData = staticData;
        _realtime = realtime;
        _mobility = mobility;
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
            if (MobilityProviderIds.TryGetValue(t.Id, out var mobilityDbId))
            {
                if (countryName is not null &&
                    !_mobility.HasStationsInCountry(mobilityDbId, countryName))
                    continue;

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

    // ── Routes — still from local static data ────────────────────────────

    /// <summary>
    /// Returns routes from loaded GTFS data (used for vehicle icon colours on the map).
    /// Transitland does not expose a simple routes-list endpoint on the free tier,
    /// so this still reads from the local GTFS cache when available.
    /// Returns an empty list when no GTFS is loaded — the map still works,
    /// vehicles just show without route colours.
    /// </summary>
    public List<RouteDto> GetAllRoutes(int? countryId = null)
    {
        var query = _db.TransitOperators
            .Where(o => o.GtfsFeedUrl != null)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);

        var loaded = query.Select(o => o.Id).ToList();

        return loaded
            .SelectMany(id => _staticData.GetRoutes(id))
            .ToList();
    }

    // ── Vehicles (still from local GTFS-RT) ──────────────────────────────

    public List<VehicleDto> GetAllVehicles(int? countryId = null)
    {
        if (!countryId.HasValue)
            return _realtime.GetAllVehicles();

        var operatorIds = _db.TransitOperators
            .Where(o => o.CountryId == countryId.Value)
            .Select(o => o.Id)
            .ToList();

        return operatorIds
            .SelectMany(id => _realtime.GetVehicles(id))
            .ToList();
    }

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

    // ── Trip detail (still from local GTFS data) ──────────────────────────

    public async Task<TripDetailDto?> GetTripDetailAsync(string tripId)
    {
        var operatorId = FindOperatorForTrip(tripId);
        if (operatorId is null)
        {
            _logger.LogWarning("[Trip] Trip {TripId} not found in any operator", tripId);
            return null;
        }

        var stops = await _staticData.GetTripStopsAsync(operatorId.Value, tripId);
        if (stops.Count == 0) return null;

        var tripRouteMap = _staticData.GetTripRouteMap(operatorId.Value);
        var routeId = tripRouteMap?.TryGetValue(tripId, out var rid) == true ? rid : "";
        var routeType = _staticData.GetRouteType(operatorId.Value, routeId);
        var shortName = _staticData.GetRouteName(operatorId.Value, routeId);
        var headsign = stops.Last().StopName;

        var vehicle = _realtime.GetVehicleByTrip(operatorId.Value, tripId);
        var isTracked = vehicle is not null;

        int nowMins = DateTime.Now.Hour * 60 + DateTime.Now.Minute - 1;
        int currentIdx = 0;

        for (int i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            int stopMins = TimeToMinutes(stop.ScheduledTime);

            var updates = _realtime.GetTripUpdates(operatorId.Value, tripId);
            if (updates is not null)
            {
                var stu = updates.FirstOrDefault(u => u.StopId == stop.StopId)
                       ?? updates.FirstOrDefault(u => u.StopSequence == stop.Sequence);

                if (stu is not null)
                {
                    stop.DelayMinutes = (int)Math.Round(stu.DelaySeconds / 60.0);
                    var parts = stop.ScheduledTime.Split(':');
                    if (parts.Length >= 2
                        && int.TryParse(parts[0], out int h)
                        && int.TryParse(parts[1], out int m))
                    {
                        int estMins = h * 60 + m + stop.DelayMinutes.Value;
                        stop.EstimatedTime = $"{estMins / 60:D2}:{estMins % 60:D2}";
                    }
                }
            }

            if (stopMins < nowMins)
            {
                stop.IsPassed = true;
                currentIdx = i + 1;
            }
        }

        currentIdx = Math.Min(currentIdx, stops.Count - 1);

        return new TripDetailDto
        {
            TripId = tripId,
            RouteId = routeId,
            ShortName = shortName,
            Headsign = headsign,
            RouteType = routeType,
            Stops = stops,
            CurrentStopIndex = currentIdx,
            VehicleLat = vehicle?.Lat ?? 0,
            VehicleLon = vehicle?.Lon ?? 0,
            IsRealtime = isTracked,
            VehicleId = vehicle?.VehicleId,
            BlockId = vehicle?.BlockId,
        };
    }

    // ── Startup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called once on server startup.
    /// Only loads local GTFS data for operators that still have a GtfsFeedUrl set
    /// (used for trip detail lookups). Stops + schedules now come from Transitland.
    /// </summary>
    public async Task InitialiseAsync()
    {
        var operators = await _db.TransitOperators
            .Include(o => o.Country)
            .Include(o => o.City)
            .Include(o => o.TransportTypes)
            .ToListAsync();

        _logger.LogInformation("[Startup] Loading {Count} operators", operators.Count);

        foreach (var op in operators)
        {
            if (!string.IsNullOrEmpty(op.GtfsFeedUrl))
            {
                var usedTypes = await _staticData.LoadOperatorAsync(op);

                var matchingTypes = await _db.TransportTypes
                    .Where(t => usedTypes.Contains(t.GtfsRouteType))
                    .ToListAsync();

                foreach (var type in matchingTypes)
                    if (!op.TransportTypes.Any(t => t.Id == type.Id))
                        op.TransportTypes.Add(type);

                await _db.SaveChangesAsync();
            }

            if (!string.IsNullOrEmpty(op.GtfsRealtimeFeedUrl))
                _realtime.RegisterOperator(op);
        }

        _logger.LogInformation("[Startup] All operators loaded");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private int? FindOperatorForTrip(string tripId)
    {
        var operatorIds = _db.TransitOperators
            .Where(o => o.GtfsFeedUrl != null)
            .Select(o => o.Id)
            .ToList();

        foreach (var id in operatorIds)
        {
            var map = _staticData.GetTripRouteMap(id);
            if (map?.ContainsKey(tripId) == true)
                return id;
        }
        return null;
    }

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

    private static int TimeToMinutes(string t)
    {
        var p = t.Split(':');
        return p.Length >= 2
               && int.TryParse(p[0], out int h)
               && int.TryParse(p[1], out int m)
            ? h * 60 + m : 0;
    }
}