using GetThereAPI.Entities;
using GetThereAPI.Parsers.Realtime;
using GetThereShared.Dtos;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace GetThereAPI.Managers;

/// <summary>
/// Background service that polls GTFS-RT feeds for all operators
/// every 10 seconds and caches the results in memory.
///
/// How it works:
///   1. Runs forever as a hosted background service
///   2. Every 10 seconds fetches raw bytes from each operator's
///      realtime feed URL
///   3. Passes bytes to the correct parser (proto, JSON, SIRI, REST)
///   4. Stores decoded vehicles in memory
///   5. Any request for vehicles just reads from that in-memory cache
///      — instant response, no waiting for ZET
///
/// ZET sees exactly 1 request per 10 seconds regardless of how many
/// users are using the app at the same time.
/// </summary>
public class RealtimeManager : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly StaticDataManager _staticData;
    private readonly ILogger<RealtimeManager> _logger;

    private const int PollIntervalSeconds = 10;

    // ── In-memory cache ───────────────────────────────────────────────────

    // operatorId → list of vehicles currently on the map (GPS position known)
    private readonly ConcurrentDictionary<int, List<VehicleDto>> _vehicles = new();

    // operatorId → tripId → StopTimeUpdates (delay predictions per stop)
    // Used by ScheduleManager to annotate departures with realtime delays.
    // Never sent to the app directly — server merges this before responding.
    private readonly ConcurrentDictionary<int, Dictionary<string, List<StopTimeUpdate>>> _tripUpdates = new();

    // Which operators to poll — set by OperatorManager on startup
    private readonly ConcurrentDictionary<int, TransitOperator> _operators = new();

    public RealtimeManager(
        IHttpClientFactory httpFactory,
        StaticDataManager staticData,
        ILogger<RealtimeManager> logger)
    {
        _httpFactory = httpFactory;
        _staticData = staticData;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all vehicles currently on the map for an operator.
    /// Only vehicles with a real GPS position are included.
    /// </summary>
    public List<VehicleDto> GetVehicles(int operatorId)
        => _vehicles.TryGetValue(operatorId, out var v) ? v : [];

    /// <summary>
    /// Returns all vehicles across all operators.
    /// Used by the map endpoint which shows all operators at once.
    /// </summary>
    public List<VehicleDto> GetAllVehicles()
        => _vehicles.Values.SelectMany(v => v).ToList();

    /// <summary>
    /// Returns the stop time updates (delay predictions) for a trip.
    /// Used by ScheduleManager to merge realtime delays into schedules.
    /// </summary>
    public List<StopTimeUpdate>? GetTripUpdates(int operatorId, string tripId)
    {
        if (!_tripUpdates.TryGetValue(operatorId, out var map)) return null;
        return map.TryGetValue(tripId, out var updates) ? updates : null;
    }

    /// <summary>
    /// Returns the vehicle currently running a specific trip.
    /// Used by ScheduleManager to check if a trip is being tracked.
    /// </summary>
    public VehicleDto? GetVehicleByTrip(int operatorId, string tripId)
        => _vehicles.TryGetValue(operatorId, out var list)
            ? list.FirstOrDefault(v => v.TripId == tripId)
            : null;

    /// <summary>
    /// Registers an operator for realtime polling.
    /// Called by OperatorManager on startup for every operator
    /// that has a realtime feed URL.
    /// </summary>
    public void RegisterOperator(TransitOperator op)
    {
        if (string.IsNullOrEmpty(op.GtfsRealtimeFeedUrl)) return;
        _operators[op.Id] = op;
        _logger.LogInformation("[Realtime] Registered operator {Name}", op.Name);
    }

    /// <summary>Removes an operator from polling.</summary>
    public void UnregisterOperator(int operatorId)
    {
        _operators.TryRemove(operatorId, out _);
        _vehicles.TryRemove(operatorId, out _);
        _tripUpdates.TryRemove(operatorId, out _);
    }

    // ── Background loop ───────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Realtime] Background polling started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAllOperatorsAsync();
            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("[Realtime] Background polling stopped");
    }

    private async Task PollAllOperatorsAsync()
    {
        // Poll all operators in parallel — one slow feed doesn't block others
        var tasks = _operators.Values.Select(op => PollOperatorAsync(op));
        await Task.WhenAll(tasks);
    }

    private async Task PollOperatorAsync(TransitOperator op)
    {
        try
        {
            // Build the HTTP request (handles API key auth, bearer tokens etc.)
            using var request = BuildRequest(op);
            using var http = _httpFactory.CreateClient();
            using var response = await http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Realtime:{Name}] HTTP {Code}",
                    op.Name, (int)response.StatusCode);
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();

            // Get the trip→route map so parser can resolve routeId from tripId
            var tripRouteMap = _staticData.GetTripRouteMap(op.Id);

            // Pick the right parser based on the operator's feed format
            var parser = RealtimeParserFactory.GetParser(op);
            var parsed = await parser.ParseAsync(bytes, op, tripRouteMap);

            // Separate vehicles with GPS from trip-update-only entries
            // (trip updates exist but vehicle hasn't started transmitting GPS yet)
            var mapVehicles = new List<VehicleDto>();
            var tripUpdateMap = new Dictionary<string, List<StopTimeUpdate>>(StringComparer.Ordinal);

            foreach (var v in parsed)
            {
                // Collect stop time updates (delay predictions) for all trips
                if (v.TripId != null && v.StopTimeUpdates?.Count > 0)
                    tripUpdateMap[v.TripId] = v.StopTimeUpdates
                        .Select(u => new StopTimeUpdate
                        {
                            StopId = u.StopId,
                            StopSequence = u.StopSequence,
                            DelaySeconds = u.DelaySeconds,
                            ArrivalUnix = u.ArrivalUnix,
                        }).ToList();

                // Only add to map if it has a real GPS position
                if (v.IsScheduledOnly) continue;

                var routeId = v.RouteId ?? "";
                var routeType = _staticData.GetRouteType(op.Id, routeId);
                var shortName = _staticData.GetRouteName(op.Id, routeId);

                mapVehicles.Add(new VehicleDto
                {
                    VehicleId = v.VehicleId,
                    TripId = v.TripId,
                    RouteId = routeId,
                    RouteShortName = shortName,
                    RouteType = routeType,
                    Lat = v.Lat,
                    Lon = v.Lon,
                    Bearing = v.Bearing,
                    IsRealtime = true,
                    BlockId = v.TripId?.Split('_') is { Length: > 2 } p ? p[2] : null,
                });
            }

            // Update cache atomically
            _vehicles[op.Id] = mapVehicles;
            _tripUpdates[op.Id] = tripUpdateMap;

            _logger.LogDebug("[Realtime:{Name}] {Count} vehicles, {Updates} trip updates",
                op.Name, mapVehicles.Count, tripUpdateMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Realtime:{Name}] Poll error", op.Name);
        }
    }

    // ── HTTP request builder ──────────────────────────────────────────────

    private static HttpRequestMessage BuildRequest(TransitOperator op)
    {
        var url = op.GtfsRealtimeFeedUrl!;

        // API key in query string: "paramName:value"
        if (op.RealtimeAuthType == "API_KEY_QUERY"
            && !string.IsNullOrEmpty(op.RealtimeAuthConfig))
        {
            var parts = op.RealtimeAuthConfig.Split(':', 2);
            if (parts.Length == 2)
                url += (url.Contains('?') ? "&" : "?")
                     + $"{parts[0]}={Uri.EscapeDataString(parts[1])}";
        }

        var msg = new HttpRequestMessage(HttpMethod.Get, url);

        // API key or bearer token in header: "HeaderName:value"
        if ((op.RealtimeAuthType == "API_KEY_HEADER" || op.RealtimeAuthType == "BEARER")
            && !string.IsNullOrEmpty(op.RealtimeAuthConfig))
        {
            var parts = op.RealtimeAuthConfig.Split(':', 2);
            if (parts.Length == 2)
                msg.Headers.TryAddWithoutValidation(parts[0], parts[1]);
        }

        return msg;
    }
}

// ── Internal model — never sent to the app ────────────────────────────────
// This lives here rather than in Dtos because it's server-internal only.
// The app never sees StopTimeUpdates — ScheduleManager merges them before
// building the response.

public class StopTimeUpdate
{
    public string? StopId { get; set; }
    public int StopSequence { get; set; }
    /// <summary>Delay in seconds. Negative = early, positive = late.</summary>
    public int DelaySeconds { get; set; }
    /// <summary>Absolute Unix arrival timestamp (optional).</summary>
    public long ArrivalUnix { get; set; }
}