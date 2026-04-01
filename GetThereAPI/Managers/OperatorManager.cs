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

    // ── Operators ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all operators from the database.
    /// Auth fields are intentionally excluded — never sent to clients.
    /// </summary>
    public async Task<List<OperatorDto>> GetAllOperatorsAsync()
    {
        return await _db.TransitOperators
            .Include(o => o.Country)
            .Include(o => o.City)
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
    /// Returns all stops for all loaded operators combined.
    /// The app calls this once on startup and caches the result locally.
    /// </summary>
    public List<StopDto> GetAllStops()
    {
        var loaded = _db.TransitOperators
            .Where(o => o.GtfsFeedUrl != null)
            .Select(o => o.Id)
            .ToList();

        return loaded
            .SelectMany(id => _staticData.GetStops(id))
            .ToList();
    }

    /// <summary>Returns stops for a single operator.</summary>
    public List<StopDto> GetStops(int operatorId)
        => _staticData.GetStops(operatorId);

    // ── Routes ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all routes for all loaded operators combined.
    /// Used by the map to draw route shapes and colour vehicle icons.
    /// </summary>
    public List<RouteDto> GetAllRoutes()
    {
        var loaded = _db.TransitOperators
            .Where(o => o.GtfsFeedUrl != null)
            .Select(o => o.Id)
            .ToList();

        return loaded
            .SelectMany(id => _staticData.GetRoutes(id))
            .ToList();
    }

    // ── Vehicles ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all vehicles currently on the map across all operators.
    /// Data is served from RealtimeManager's in-memory cache —
    /// it was last updated at most 10 seconds ago.
    /// </summary>
    public List<VehicleDto> GetAllVehicles()
        => _realtime.GetAllVehicles();

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