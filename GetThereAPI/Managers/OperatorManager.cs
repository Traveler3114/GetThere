using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// Handles all transit-related business logic.
/// Sits between the controller (HTTP) and the two data managers (static + realtime).
///
/// StaticDataManager  = stops, routes, trip maps, schedules (from GTFS ZIP)
/// RealtimeManager    = live vehicle positions (from GTFS-RT feed)
/// OperatorManager    = answers requests by reading from both of the above
/// </summary>
public class OperatorManager
{
    private readonly AppDbContext _db;
    private readonly StaticDataManager _staticData;
    private readonly RealtimeManager _realtime;
    private readonly ILogger<OperatorManager> _logger;

    public OperatorManager(
        AppDbContext db,
        StaticDataManager staticData,
        RealtimeManager realtime,
        ILogger<OperatorManager> logger)
    {
        _db = db;
        _staticData = staticData;
        _realtime = realtime;
        _logger = logger;
    }

    // ── Hardcoded ticketable operator definitions ─────────────────────────

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
            Description = "Nextbike Zagreb — city bike sharing service.",
            City = "Zagreb", Country = "Croatia", IsMock = true,
        },
    ];

    /// <summary>
    /// Returns operators available for ticket purchase, filtered by country.
    /// An operator is ticketable if its TicketApiBaseUrl is non-empty OR
    /// it is in the hardcoded mock list (ZET, HZPP, Bajs).
    /// </summary>
    public async Task<List<TicketableOperatorDto>> GetTicketableOperatorsAsync(int? countryId)
    {
        // Enrich hardcoded list with LogoUrl from the DB for transit operators
        var transitIds = new[] { 1, 2 };
        var dbOps = await _db.TransitOperators
            .Where(o => transitIds.Contains(o.Id))
            .Select(o => new { o.Id, o.LogoUrl })
            .ToListAsync();

        var logoMap = dbOps.ToDictionary(o => o.Id, o => o.LogoUrl);

        var result = TicketableList.Select(t =>
        {
            // Clone so we don't mutate the static list
            var dto = new TicketableOperatorDto
            {
                Id          = t.Id,
                Name        = t.Name,
                Type        = t.Type,
                Color       = t.Color,
                Description = t.Description,
                City        = t.City,
                Country     = t.Country,
                IsMock      = t.IsMock,
                LogoUrl     = logoMap.TryGetValue(t.Id, out var url) ? url : t.LogoUrl,
            };
            return dto;
        }).ToList();

        // When countryId is provided keep only operators in that country
        if (countryId.HasValue)
        {
            var countryName = await _db.Countries
                .Where(c => c.Id == countryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            if (countryName is not null)
                result = result.Where(t => t.Country == countryName).ToList();
            else
                result = [];
        }

        return result;
    }

    // ── Operators ─────────────────────────────────────────────────────────
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

    /// <summary>
    /// Returns operators from the database, optionally filtered by country.
    /// Auth fields are intentionally excluded — never sent to clients.
    /// </summary>
    /// <param name="countryId">
    /// When provided only operators belonging to this country are returned.
    /// When null all operators are returned (backwards compatible).
    /// </param>
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

    // ── Stops ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all stops for all loaded operators, optionally filtered by country.
    /// The app calls this once on startup and caches the result locally.
    /// </summary>
    /// <param name="countryId">
    /// When provided only stops for operators in this country are returned.
    /// When null stops for all operators are returned (backwards compatible).
    /// </param>
    public List<StopDto> GetAllStops(int? countryId = null)
    {
        var query = _db.TransitOperators
            .Where(o => o.GtfsFeedUrl != null)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);

        var loaded = query.Select(o => o.Id).ToList();

        return loaded
            .SelectMany(id => _staticData.GetStops(id))
            .ToList();
    }

    /// <summary>Returns stops for a single operator.</summary>
    public List<StopDto> GetStops(int operatorId)
        => _staticData.GetStops(operatorId);

    // ── Routes ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all routes for all loaded operators, optionally filtered by country.
    /// Used by the map to draw route shapes and colour vehicle icons.
    /// </summary>
    /// <param name="countryId">
    /// When provided only routes for operators in this country are returned.
    /// When null routes for all operators are returned (backwards compatible).
    /// </param>
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

    // ── Vehicles ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all vehicles currently on the map, optionally filtered by country.
    /// Data is served from RealtimeManager's in-memory cache —
    /// it was last updated at most 10 seconds ago.
    /// </summary>
    /// <param name="countryId">
    /// When provided only vehicles for operators in this country are returned.
    /// When null vehicles for all operators are returned (backwards compatible).
    /// </param>
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

    // ── Stop schedule ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns today's departures for a stop, with realtime delays merged in.
    /// Called when a user taps a stop on the map.
    /// </summary>
    public async Task<StopScheduleDto?> GetStopScheduleAsync(string stopId)
    {
        // Find which operator owns this stop
        var operatorId = FindOperatorForStop(stopId);
        if (operatorId is null)
        {
            _logger.LogWarning("[Schedule] Stop {StopId} not found in any operator", stopId);
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var groups = await _staticData.GetStopScheduleAsync(operatorId.Value, stopId, today);

        // Merge realtime delays into each departure
        foreach (var group in groups)
        {
            foreach (var dep in group.Departures)
            {
                var updates = _realtime.GetTripUpdates(operatorId.Value, dep.TripId);
                if (updates is null) continue;

                var stu = updates.FirstOrDefault(u => u.StopId == stopId);
                if (stu is null) continue;

                var vehicle = _realtime.GetVehicleByTrip(operatorId.Value, dep.TripId);
                dep.IsRealtime = vehicle is not null;

                int delaySec = stu.DelaySeconds;
                dep.DelayMinutes = (int)Math.Round(delaySec / 60.0);

                // Calculate estimated time from scheduled + delay
                var parts = dep.ScheduledTime.Split(':');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out int h)
                    && int.TryParse(parts[1], out int m))
                {
                    int estMins = h * 60 + m + dep.DelayMinutes.Value;
                    estMins = Math.Max(0, estMins);
                    dep.EstimatedTime = $"{estMins / 60:D2}:{estMins % 60:D2}";
                }
            }
        }

        // Get stop name from cached stops
        var stop = _staticData.GetStops(operatorId.Value)
            .FirstOrDefault(s => s.StopId == stopId);

        return new StopScheduleDto
        {
            StopId = stopId,
            StopName = stop?.Name ?? stopId,
            Groups = groups,
        };
    }

    // ── Trip detail ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full stop sequence for a trip with realtime delays.
    /// Called when a user taps a vehicle on the map.
    /// </summary>
    public async Task<TripDetailDto?> GetTripDetailAsync(string tripId)
    {
        // Find which operator owns this trip
        var operatorId = FindOperatorForTrip(tripId);
        if (operatorId is null)
        {
            _logger.LogWarning("[Trip] Trip {TripId} not found in any operator", tripId);
            return null;
        }

        var stops = await _staticData.GetTripStopsAsync(operatorId.Value, tripId);
        if (stops.Count == 0) return null;

        // Get route info
        var tripRouteMap = _staticData.GetTripRouteMap(operatorId.Value);
        var routeId = tripRouteMap?.TryGetValue(tripId, out var rid) == true ? rid : "";
        var routeType = _staticData.GetRouteType(operatorId.Value, routeId);
        var shortName = _staticData.GetRouteName(operatorId.Value, routeId);
        var headsign = stops.Last().StopName;

        // Get realtime vehicle position and delay data
        var vehicle = _realtime.GetVehicleByTrip(operatorId.Value, tripId);
        var isTracked = vehicle is not null;

        // Mark passed stops and merge delays
        int nowMins = DateTime.Now.Hour * 60 + DateTime.Now.Minute - 1;
        int currentIdx = 0;

        for (int i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            int stopMins = TimeToMinutes(stop.ScheduledTime);

            // Merge realtime delay if available
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

    // ── Startup: load all operators ───────────────────────────────────────

    /// <summary>
    /// Called once on server startup (from Program.cs).
    /// Loads static data for all operators and registers them for
    /// realtime polling.
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

                // Link operator to its transport types in the join table
                // Only links types that have been manually added to TransportTypes by admin
                // Skips any route type not in the DB — admin controls what's supported
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

    /// <summary>
    /// Finds which operator a stop belongs to by checking each operator's
    /// loaded stop list. Returns null if not found.
    /// </summary>
    private int? FindOperatorForStop(string stopId)
    {
        var operatorIds = _db.TransitOperators
            .Where(o => o.GtfsFeedUrl != null)
            .Select(o => o.Id)
            .ToList();

        foreach (var id in operatorIds)
        {
            var stops = _staticData.GetStops(id);
            if (stops.Any(s => s.StopId == stopId))
                return id;
        }
        return null;
    }

    /// <summary>
    /// Finds which operator a trip belongs to by checking each operator's
    /// trip→route map.
    /// </summary>
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

    private static int TimeToMinutes(string t)
    {
        var p = t.Split(':');
        return p.Length >= 2
               && int.TryParse(p[0], out int h)
               && int.TryParse(p[1], out int m)
            ? h * 60 + m : 0;
    }
}