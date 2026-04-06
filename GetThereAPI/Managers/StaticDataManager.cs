using GetThereAPI.Entities;
using GetThereAPI.Parsers.Realtime;
using GetThereShared.Dtos;
using System.Collections.Concurrent;

namespace GetThereAPI.Managers;

/// <summary>
/// Loads and caches GTFS static data for all operators.
///
/// Responsibilities:
///   - Downloads the feed ZIP on startup (into memory, no disk I/O)
///   - Delegates all parsing to the correct IStaticDataParser via factory
///   - Serves stops, routes, trip maps instantly from RAM
///   - Provides on-demand schedule and trip detail (uses cached ZIP bytes)
///
/// Does NOT know anything about CSV formats — that's the parser's job.
/// </summary>
public class StaticDataManager
{
    private readonly IHttpClientFactory           _httpFactory;
    private readonly ILogger<StaticDataManager>   _logger;

    // ── In-memory cache, keyed by operatorId ─────────────────────────────

    private readonly ConcurrentDictionary<int, List<StopDto>>   _stops      = new();
    private readonly ConcurrentDictionary<int, List<RouteDto>>  _routes     = new();
    private readonly ConcurrentDictionary<int, Dictionary<string, string>> _tripRouteMap = new();
    private readonly ConcurrentDictionary<int, Dictionary<string, string>> _routeNames   = new();
    private readonly ConcurrentDictionary<int, Dictionary<string, int>>    _routeTypes   = new();

    // Raw ZIP bytes kept for on-demand schedule/trip parsing
    private readonly ConcurrentDictionary<int, byte[]> _zipBytes = new();

    // Parser instances per operator (resolved once at load time)
    private readonly ConcurrentDictionary<int, IStaticDataParser> _parsers = new();

    // Trip stop cache — stop_times.txt only scanned once per trip
    private readonly ConcurrentDictionary<string, List<TripStopDto>> _tripStopCache = new();
    private readonly ConcurrentDictionary<string, List<(int OperatorId, string StopId)>> _stationCoverage = new();

    public StaticDataManager(
        IHttpClientFactory         httpFactory,
        ILogger<StaticDataManager> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    // ── Public read API ───────────────────────────────────────────────────

    public List<StopDto>  GetStops(int operatorId)
        => _stops.TryGetValue(operatorId, out var v) ? v : [];

    public List<RouteDto> GetRoutes(int operatorId)
        => _routes.TryGetValue(operatorId, out var v) ? v : [];

    /// <summary>Used by RealtimeManager to resolve routeId from tripId.</summary>
    public Dictionary<string, string>? GetTripRouteMap(int operatorId)
        => _tripRouteMap.TryGetValue(operatorId, out var v) ? v : null;

    public int GetRouteType(int operatorId, string routeId)
        => _routeTypes.TryGetValue(operatorId, out var m)
           && m.TryGetValue(routeId, out var t) ? t : 3;

    public string GetRouteName(int operatorId, string routeId)
        => _routeNames.TryGetValue(operatorId, out var m)
           && m.TryGetValue(routeId, out var n) ? n : routeId;

    public bool IsLoaded(int operatorId) => _stops.ContainsKey(operatorId);

    public string BuildStationKeyForStop(StopDto stop)
    {
        if (!string.IsNullOrWhiteSpace(stop.StationKey))
            return stop.StationKey!;

        if (!string.IsNullOrWhiteSpace(stop.ParentStationId))
            return $"parent:{stop.ParentStationId!.Trim()}";

        var normalizedName = NormalizeName(stop.Name);
        return $"geo:{normalizedName}:{Math.Round(stop.Lat, 4):F4}:{Math.Round(stop.Lon, 4):F4}";
    }

    public List<(int OperatorId, string StopId)> GetStationCoverage(string stationKey)
        => _stationCoverage.TryGetValue(stationKey, out var v) ? v : [];

    // ── Load ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads and parses static data for one operator.
    /// Called by OperatorManager on server startup and weekly thereafter.
    /// </summary>
    public async Task<HashSet<int>> LoadOperatorAsync(TransitOperator op)
    {
        if (string.IsNullOrEmpty(op.GtfsFeedUrl))
        {
            _logger.LogWarning("[Static:{Name}] No GtfsFeedUrl set, skipping", op.Name);
            return [];
        }

        _logger.LogInformation("[Static:{Name}] Downloading from {Url}", op.Name, op.GtfsFeedUrl);

        try
        {
            var http = _httpFactory.CreateClient();
            var zipBytes = await http.GetByteArrayAsync(op.GtfsFeedUrl);
            _logger.LogInformation("[Static:{Name}] Downloaded {Kb} KB",
                op.Name, zipBytes.Length / 1024);

            // Pick the correct parser for this operator's format
            var parser = StaticParserFactory.GetParser(op);
            _parsers[op.Id] = parser;

            // Parse everything upfront — all subsequent reads are from RAM
            var stops = await parser.ParseStopsAsync(zipBytes);
            var routes = await parser.ParseRoutesAsync(zipBytes);
            var tripRouteMap = await parser.ParseTripRouteMapAsync(zipBytes);
            var usedTypes = await parser.ParseUsedRouteTypesAsync(zipBytes);

            // Build quick-lookup maps for route names and types
            var routeNames = routes.ToDictionary(r => r.RouteId, r => r.ShortName);
            var routeTypes = routes.ToDictionary(r => r.RouteId, r => r.RouteType);

            // Store in memory
            _stops[op.Id] = stops;
            _routes[op.Id] = routes;
            _tripRouteMap[op.Id] = tripRouteMap;
            _routeNames[op.Id] = routeNames;
            _routeTypes[op.Id] = routeTypes;
            _zipBytes[op.Id] = zipBytes;

            // Clear trip cache since we have fresh data
            _tripStopCache.Clear();
            RebuildStationCoverage();

            _logger.LogInformation(
                "[Static:{Name}] Loaded {S} stops, {R} routes, {T} trips",
                op.Name, stops.Count, routes.Count, tripRouteMap.Count);

            return usedTypes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Static:{Name}] Failed to load", op.Name);
            return [];
        }
    }

    /// <summary>Removes all cached data for an operator.</summary>
    public void UnloadOperator(int operatorId)
    {
        _stops.TryRemove(operatorId, out _);
        _routes.TryRemove(operatorId, out _);
        _tripRouteMap.TryRemove(operatorId, out _);
        _routeNames.TryRemove(operatorId, out _);
        _routeTypes.TryRemove(operatorId, out _);
        _zipBytes.TryRemove(operatorId, out _);
        _parsers.TryRemove(operatorId, out _);
        RebuildStationCoverage();
    }

    // ── On-demand schedule parsing ────────────────────────────────────────

    /// <summary>
    /// Returns departures for a stop on a given date.
    /// Called by OperatorManager when a user taps a stop.
    /// </summary>
    public async Task<List<DepartureGroupDto>> GetStopScheduleAsync(
        int operatorId, string stopId, DateOnly date)
    {
        if (!_zipBytes.TryGetValue(operatorId, out var bytes)) return [];
        if (!_parsers.TryGetValue(operatorId, out var parser)) return [];

        return await parser.ParseStopScheduleAsync(bytes, stopId, date);
    }

    /// <summary>
    /// Returns the full ordered stop sequence for a trip.
    /// Result is cached after first call.
    /// Called by OperatorManager when a user taps a vehicle.
    /// </summary>
    public async Task<List<TripStopDto>> GetTripStopsAsync(int operatorId, string tripId)
    {
        if (_tripStopCache.TryGetValue(tripId, out var cached)) return cached;
        if (!_zipBytes.TryGetValue(operatorId, out var bytes)) return [];
        if (!_parsers.TryGetValue(operatorId, out var parser)) return [];

        var result = await parser.ParseTripStopsAsync(bytes, tripId);
        _tripStopCache.TryAdd(tripId, result);
        return result;
    }

    private void RebuildStationCoverage()
    {
        _stationCoverage.Clear();

        foreach (var kvp in _stops)
        {
            var operatorId = kvp.Key;
            foreach (var stop in kvp.Value)
            {
                if (string.IsNullOrWhiteSpace(stop.StopId)) continue;
                var stationKey = BuildStationKeyForStop(stop);
                if (string.IsNullOrWhiteSpace(stationKey)) continue;

                _stationCoverage.AddOrUpdate(
                    stationKey,
                    _ => [(operatorId, stop.StopId)],
                    (_, existing) =>
                    {
                        if (!existing.Any(x => x.OperatorId == operatorId && x.StopId == stop.StopId))
                            existing.Add((operatorId, stop.StopId));
                        return existing;
                    });
            }
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        var chars = name.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray();
        var collapsed = string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(collapsed) ? "unknown" : collapsed;
    }
}
