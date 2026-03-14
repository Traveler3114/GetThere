using GetThere.Helpers;
using GetThere.Services.Realtime;
using GetThereShared.Dtos;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace GetThere.Services;

/// <summary>
/// Handles GTFS static data (install, remove, parse stops/routes) and
/// realtime vehicle polling for any operator.
///
/// Format-specific realtime parsing is delegated to IRealtimeParser
/// implementations selected by RealtimeParserFactory.
/// </summary>
public class GtfsService
{
    private const string InstalledKey = "installed_operator_ids";

    // Prevents concurrent install/read on the same zip file
    private static readonly SemaphoreSlim _zipLock = new(1, 1);
    private const string RealtimeUrlsKey = "realtime_feed_urls";
    private const string TripMapKeyPrefix = "trip_route_map_";

    private readonly HttpClient _http;

    public GtfsService(HttpClient http) => _http = http;

    // ────────────────────────────────────────────────────────────────────
    // Install / Remove
    // ────────────────────────────────────────────────────────────────────

    public async Task<bool> InstallAsync(TransitOperatorDto op, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(op.GtfsFeedUrl)) return false;

        var dir = GetOperatorDir(op.Id);
        var zipPath = Path.Combine(dir, "gtfs.zip");
        Directory.CreateDirectory(dir);

        // Download static GTFS zip — lock so ParseStopsAsync/ParseRoutesAsync can't race
        await _zipLock.WaitAsync();
        try
        {
            using var response = await _http.GetAsync(op.GtfsFeedUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;
            var buffer = new byte[81920];
            long downloaded = 0;
            await using var src = await response.Content.ReadAsStreamAsync();
            await using var dest = File.Create(zipPath);
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0) progress?.Report((double)downloaded / total);
            }
        }
        finally
        {
            _zipLock.Release();
        }

        // Cache realtime URL and operator config
        MarkInstalled(op.Id);
        if (!string.IsNullOrEmpty(op.GtfsRealtimeFeedUrl))
            SaveOperatorConfig(op);

        // Build and cache trip→route map from trips.txt
        await BuildTripRouteMapAsync(op.Id);

        return true;
    }

    public void Remove(int operatorId)
    {
        var dir = GetOperatorDir(operatorId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        MarkRemoved(operatorId);
        RemoveOperatorConfig(operatorId);
        Preferences.Remove($"{TripMapKeyPrefix}{operatorId}");
    }

    public bool IsInstalled(int operatorId) => GetInstalledIds().Contains(operatorId);
    public bool HasRealtime(int operatorId) => GetSavedOperator(operatorId) != null;

    public string GetOperatorDir(int operatorId)
        => Path.Combine(FileSystem.AppDataDirectory, "gtfs", operatorId.ToString());

    public long GetSizeBytes(int operatorId)
    {
        var zip = Path.Combine(GetOperatorDir(operatorId), "gtfs.zip");
        return File.Exists(zip) ? new FileInfo(zip).Length : 0;
    }

    // ────────────────────────────────────────────────────────────────────
    // Trip → Route map  (built once at install, cached in Preferences)
    // ────────────────────────────────────────────────────────────────────

    private async Task BuildTripRouteMapAsync(int operatorId)
    {
        using var zip = OpenZip(operatorId);
        var entry = zip?.GetEntry("trips.txt");
        if (entry is null) return;

        await using var stream = entry.Open();
        var rows = await ParseCsvAsync(stream);

        var map = new Dictionary<string, string>(rows.Count);
        foreach (var row in rows)
        {
            var tripId = Get(row, "trip_id");
            var routeId = Get(row, "route_id");
            if (!string.IsNullOrEmpty(tripId) && !string.IsNullOrEmpty(routeId))
                map.TryAdd(tripId, routeId);
        }

        Preferences.Set($"{TripMapKeyPrefix}{operatorId}", JsonSerializer.Serialize(map));
        Trace.WriteLine($"[GTFS:{operatorId}] Built trip→route map: {map.Count} entries");
    }

    private Dictionary<string, string>? GetTripRouteMap(int operatorId)
    {
        var json = Preferences.Get($"{TripMapKeyPrefix}{operatorId}", null as string);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }

    // ────────────────────────────────────────────────────────────────────
    // Realtime Vehicles
    // ────────────────────────────────────────────────────────────────────

    public async Task<List<VehiclePositionDto>> GetVehiclesAsync(TransitOperatorDto op)
    {
        if (string.IsNullOrEmpty(op.GtfsRealtimeFeedUrl)) return [];

        // Apply auth headers/params before fetching
        using var request = BuildRealtimeRequest(op);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Trace.WriteLine($"[Realtime:{op.Name}] HTTP {(int)response.StatusCode}");
            return [];
        }

        var data = await response.Content.ReadAsByteArrayAsync();
        Trace.WriteLine($"[Realtime:{op.Name}] Fetched {data.Length} bytes");

        var tripRouteMap = GetTripRouteMap(op.Id);
        var parser = RealtimeParserFactory.GetParser(op);
        return await parser.ParseAsync(data, op, tripRouteMap);
    }

    private static HttpRequestMessage BuildRealtimeRequest(TransitOperatorDto op)
    {
        var url = op.GtfsRealtimeFeedUrl!;

        // API_KEY_QUERY: append key as query param "paramName:value"
        if (op.RealtimeAuthType == "API_KEY_QUERY" && !string.IsNullOrEmpty(op.RealtimeAuthConfig))
        {
            var parts = op.RealtimeAuthConfig.Split(':', 2);
            if (parts.Length == 2)
                url += (url.Contains('?') ? "&" : "?") + $"{parts[0]}={Uri.EscapeDataString(parts[1])}";
        }

        var msg = new HttpRequestMessage(HttpMethod.Get, url);

        // API_KEY_HEADER or BEARER: add header "HeaderName:Value"
        if ((op.RealtimeAuthType == "API_KEY_HEADER" || op.RealtimeAuthType == "BEARER")
            && !string.IsNullOrEmpty(op.RealtimeAuthConfig))
        {
            var parts = op.RealtimeAuthConfig.Split(':', 2);
            if (parts.Length == 2) msg.Headers.TryAddWithoutValidation(parts[0], parts[1]);
        }

        return msg;
    }

    // ────────────────────────────────────────────────────────────────────
    // Parse Stops
    // ────────────────────────────────────────────────────────────────────

    // REPLACE ParseStopsAsync in GtfsService.cs with this:

    public async Task<List<GtfsStopDto>> ParseStopsAsync(int operatorId)
    {
        using var zip = OpenZip(operatorId);
        var entry = zip?.GetEntry("stops.txt");
        if (entry is null) return [];
        await using var stream = entry.Open();
        var rows = await ParseCsvAsync(stream);
        var stops = rows.Select(r => new GtfsStopDto
        {
            StopId = Get(r, "stop_id"),
            Name = Get(r, "stop_name"),
            Lat = ParseDouble(Get(r, "stop_lat")),
            Lon = ParseDouble(Get(r, "stop_lon")),
        }).Where(s => s.Lat != 0 && s.Lon != 0).ToList();
        ApplyStopRouteTypes(operatorId, stops);
        return stops;
    }

    // ────────────────────────────────────────────────────────────────────
    // Parse Routes
    // ────────────────────────────────────────────────────────────────────

    public async Task<List<GtfsRouteDto>> ParseRoutesAsync(int operatorId)
    {
        using var zip = OpenZip(operatorId);
        if (zip is null) return [];

        var routeEntry = zip.GetEntry("routes.txt");
        if (routeEntry is null) return [];

        await using var routeStream = routeEntry.Open();
        var routeRows = await ParseCsvAsync(routeStream);

        var routes = routeRows.Select(r => new GtfsRouteDto
        {
            RouteId = Get(r, "route_id"),
            ShortName = Get(r, "route_short_name"),
            LongName = Get(r, "route_long_name"),
            Color = string.IsNullOrWhiteSpace(Get(r, "route_color")) ? null : Get(r, "route_color"),
            RouteType = int.TryParse(Get(r, "route_type"), out var rt) ? rt : 3,
        }).ToList();

        var shapes = await ParseShapesAsync(zip);
        var routeShapeMap = await BuildRouteShapeMapAsync(zip);

        foreach (var route in routes)
            if (routeShapeMap.TryGetValue(route.RouteId, out var shapeId)
                && shapes.TryGetValue(shapeId, out var coords))
                route.Shape = coords;

        return routes;
    }

    // ────────────────────────────────────────────────────────────────────
    // Operator config cache (realtime URL + format stored together)
    // ────────────────────────────────────────────────────────────────────

    private void SaveOperatorConfig(TransitOperatorDto op)
    {
        var map = GetOperatorConfigMap();
        map[op.Id] = new OperatorRealtimeConfig
        {
            Url = op.GtfsRealtimeFeedUrl ?? "",
            Format = op.RealtimeFeedFormat,
            AuthType = op.RealtimeAuthType,
            AuthConfig = op.RealtimeAuthConfig,
            AdapterConfig = op.RealtimeAdapterConfig,
        };
        Preferences.Set(RealtimeUrlsKey, JsonSerializer.Serialize(map));
    }

    private void RemoveOperatorConfig(int operatorId)
    {
        var map = GetOperatorConfigMap();
        map.Remove(operatorId);
        Preferences.Set(RealtimeUrlsKey, JsonSerializer.Serialize(map));
    }

    private OperatorRealtimeConfig? GetSavedOperator(int operatorId)
    {
        var map = GetOperatorConfigMap();
        return map.TryGetValue(operatorId, out var cfg) ? cfg : null;
    }

    private Dictionary<int, OperatorRealtimeConfig> GetOperatorConfigMap()
    {
        var json = Preferences.Get(RealtimeUrlsKey, "{}");
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, OperatorRealtimeConfig>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Stale data from old format (Dictionary<int,string>) — wipe it and start fresh
            Trace.WriteLine("[GtfsService] Clearing stale realtime config cache");
            Preferences.Remove(RealtimeUrlsKey);
            return [];
        }
    }

    private sealed class OperatorRealtimeConfig
    {
        public string Url { get; set; } = "";
        public string Format { get; set; } = "GTFS_RT_PROTO";
        public string AuthType { get; set; } = "NONE";
        public string? AuthConfig { get; set; }
        public string? AdapterConfig { get; set; }
    }

    // ────────────────────────────────────────────────────────────────────
    // Installed IDs
    // ────────────────────────────────────────────────────────────────────

    private HashSet<int> GetInstalledIds()
    {
        var json = Preferences.Get(InstalledKey, "[]");
        return JsonSerializer.Deserialize<HashSet<int>>(json) ?? [];
    }
    private void MarkInstalled(int id) { var s = GetInstalledIds(); s.Add(id); Preferences.Set(InstalledKey, JsonSerializer.Serialize(s)); }
    private void MarkRemoved(int id) { var s = GetInstalledIds(); s.Remove(id); Preferences.Set(InstalledKey, JsonSerializer.Serialize(s)); }

    // ────────────────────────────────────────────────────────────────────
    // Zip / CSV / Shape helpers
    // ────────────────────────────────────────────────────────────────────

    private ZipArchive? OpenZip(int operatorId)
    {
        var path = Path.Combine(GetOperatorDir(operatorId), "gtfs.zip");
        if (!File.Exists(path)) return null;
        try
        {
            // Wait for any in-progress install to finish writing before opening
            _zipLock.Wait();
            _zipLock.Release();
            return ZipFile.OpenRead(path);
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"[GtfsService] Cannot open zip for op {operatorId}: {ex.Message}");
            return null;
        }
    }

    private static async Task<Dictionary<string, List<double[]>>> ParseShapesAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, List<double[]>>();
        var entry = zip.GetEntry("shapes.txt");
        if (entry is null) return result;
        await using var stream = entry.Open();
        var rows = await ParseCsvAsync(stream);
        var raw = new Dictionary<string, List<(double lon, double lat, int seq)>>();
        foreach (var row in rows)
        {
            var shapeId = Get(row, "shape_id");
            var lat = ParseDouble(Get(row, "shape_pt_lat"));
            var lon = ParseDouble(Get(row, "shape_pt_lon"));
            var seq = int.TryParse(Get(row, "shape_pt_sequence"), out var s) ? s : 0;
            if (!raw.ContainsKey(shapeId)) raw[shapeId] = [];
            raw[shapeId].Add((lon, lat, seq));
        }
        foreach (var (shapeId, points) in raw)
            result[shapeId] = points.OrderBy(p => p.seq).Select(p => new[] { p.lon, p.lat }).ToList();
        return result;
    }

    private static async Task<Dictionary<string, string>> BuildRouteShapeMapAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, string>();
        var entry = zip.GetEntry("trips.txt");
        if (entry is null) return result;
        await using var stream = entry.Open();
        var rows = await ParseCsvAsync(stream);
        foreach (var row in rows)
        {
            var routeId = Get(row, "route_id");
            var shapeId = Get(row, "shape_id");
            if (!string.IsNullOrEmpty(routeId) && !string.IsNullOrEmpty(shapeId))
                result.TryAdd(routeId, shapeId);
        }
        return result;
    }

    private static async Task<List<Dictionary<string, string>>> ParseCsvAsync(Stream stream)
    {
        var results = new List<Dictionary<string, string>>();
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync();
        if (header is null) return results;
        header = header.TrimStart('\uFEFF');
        var columns = SplitCsvLine(header);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = SplitCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count && i < values.Count; i++)
                row[columns[i].Trim()] = values[i].Trim().Trim('"');
            results.Add(row);
        }
        return results;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    private static string Get(Dictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) ? v : string.Empty;

    private static double ParseDouble(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;


    // ── Paste this region into GtfsService.cs, after ParseRoutesAsync ─────────

    // ────────────────────────────────────────────────────────────────────
    // Stop Schedule  (streams stop_times.txt — safe for 100 MB files)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns departures for <paramref name="stopId"/> on <paramref name="date"/>,
    /// grouped by route+headsign, sorted by departure time.
    /// Streams stop_times.txt line-by-line so the 100 MB file never loads fully.
    /// </summary>

    // ── Paste into GtfsService.cs, after ParseRoutesAsync ─────────────────────

    public async Task<List<StopDepartureGroupDto>> ParseStopScheduleAsync(
        int operatorId,
        string stopId,
        DateOnly date)
    {
        Trace.WriteLine($"[SCHED] ===== ParseStopScheduleAsync START op={operatorId} stop='{stopId}' date={date} =====");

        using var zip = OpenZip(operatorId);
        if (zip is null)
        {
            Trace.WriteLine($"[SCHED] FAIL: OpenZip returned null for op={operatorId}");
            return [];
        }

        // Log every entry in the zip so we know what files exist
        Trace.WriteLine($"[SCHED] Zip entries: {string.Join(", ", zip.Entries.Select(e => e.Name))}");

        var activeServiceIds = await GetActiveServiceIdsAsync(zip, date);
        Trace.WriteLine($"[SCHED] Active service_ids ({activeServiceIds.Count}): [{string.Join(", ", activeServiceIds)}]");
        if (activeServiceIds.Count == 0)
        {
            Trace.WriteLine($"[SCHED] FAIL: No active service_ids for {date} — schedule will be empty");
            return [];
        }

        var tripInfo = await BuildTripInfoAsync(zip, activeServiceIds);
        Trace.WriteLine($"[SCHED] Active trips: {tripInfo.Count} (first 5: {string.Join(", ", tripInfo.Keys.Take(5))})");
        if (tripInfo.Count == 0)
        {
            Trace.WriteLine($"[SCHED] FAIL: No active trips found");
            return [];
        }

        var departures = await StreamStopTimesAsync(zip, stopId, tripInfo);
        Trace.WriteLine($"[SCHED] Raw departures for stop '{stopId}': {departures.Count}");
        if (departures.Count == 0)
            Trace.WriteLine($"[SCHED] WARNING: 0 departures — stop ID might not match. Check exact stop_id format in stop_times.txt");

        var routeNames = await BuildRouteNameMapAsync(zip);
        Trace.WriteLine($"[SCHED] Route names loaded: {routeNames.Count}");

        var result = departures
            .GroupBy(d => (d.RouteId, d.Headsign))
            .Select(g =>
            {
                var times = g.Select(d => d.DepartureTime)
                             .OrderBy(t => t, StringComparer.Ordinal)
                             .Distinct().ToList();
                return new StopDepartureGroupDto
                {
                    RouteId = g.Key.RouteId,
                    ShortName = routeNames.TryGetValue(g.Key.RouteId, out var n) ? n : g.Key.RouteId,
                    Headsign = g.Key.Headsign,
                    Times = times,
                };
            })
            .OrderBy(g => g.Times.FirstOrDefault() ?? "99:99")
            .ToList();

        Trace.WriteLine($"[SCHED] ===== DONE: {result.Count} route groups =====");
        foreach (var g in result)
            Trace.WriteLine($"[SCHED]   Route {g.ShortName} → {g.Headsign}: {g.Times.Count} times (first={g.Times.FirstOrDefault()})");

        return result;
    }

    private static async Task<HashSet<string>> GetActiveServiceIdsAsync(ZipArchive zip, DateOnly date)
    {
        int dateInt = date.Year * 10000 + date.Month * 100 + date.Day;
        Trace.WriteLine($"[SCHED/SVC] Looking for dateInt={dateInt}");

        string dayCol = date.DayOfWeek switch
        {
            DayOfWeek.Monday => "monday",
            DayOfWeek.Tuesday => "tuesday",
            DayOfWeek.Wednesday => "wednesday",
            DayOfWeek.Thursday => "thursday",
            DayOfWeek.Friday => "friday",
            DayOfWeek.Saturday => "saturday",
            _ => "sunday"
        };
        Trace.WriteLine($"[SCHED/SVC] Day column: {dayCol}");

        var active = new HashSet<string>(StringComparer.Ordinal);

        // --- calendar.txt ---
        var calEntry = zip.GetEntry("calendar.txt");
        if (calEntry is null)
            Trace.WriteLine($"[SCHED/SVC] calendar.txt NOT FOUND in zip");
        else
        {
            await using var stream = calEntry.Open();
            using var reader = new StreamReader(stream);
            var raw = await reader.ReadToEndAsync();
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Trace.WriteLine($"[SCHED/SVC] calendar.txt: {lines.Length} lines");
            if (lines.Length > 0)
            {
                var header = lines[0].TrimStart('\uFEFF');
                Trace.WriteLine($"[SCHED/SVC] calendar.txt header raw: [{header}]");
                var cols = SplitCsvLine(header);
                Trace.WriteLine($"[SCHED/SVC] calendar.txt cols: [{string.Join("|", cols)}]");
                int iSvc = IndexOf(cols, "service_id");
                int iStart = IndexOf(cols, "start_date");
                int iEnd = IndexOf(cols, "end_date");
                int iDay = IndexOf(cols, dayCol);
                Trace.WriteLine($"[SCHED/SVC] calendar.txt col indices: svc={iSvc} start={iStart} end={iEnd} day={iDay}");

                for (int li = 1; li < lines.Length; li++)
                {
                    var line = lines[li].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    var v = SplitCsvLine(line);
                    if (iSvc < 0 || iStart < 0 || iEnd < 0 || iDay < 0) continue;
                    if (v.Count <= Math.Max(iSvc, Math.Max(iStart, Math.Max(iEnd, iDay)))) continue;
                    int.TryParse(TrimCell(v[iStart]), out int s);
                    int.TryParse(TrimCell(v[iEnd]), out int e);
                    var dayVal = iDay < v.Count ? TrimCell(v[iDay]) : "?";
                    Trace.WriteLine($"[SCHED/SVC] calendar row: svc={TrimCell(v[iSvc])} start={s} end={e} {dayCol}={dayVal} dateInt={dateInt} inRange={dateInt >= s && dateInt <= e}");
                    if (dateInt >= s && dateInt <= e && dayVal == "1")
                        active.Add(TrimCell(v[iSvc]));
                }
            }
        }

        // --- calendar_dates.txt ---
        var datesEntry = zip.GetEntry("calendar_dates.txt");
        if (datesEntry is null)
            Trace.WriteLine($"[SCHED/SVC] calendar_dates.txt NOT FOUND in zip");
        else
        {
            await using var stream = datesEntry.Open();
            using var reader = new StreamReader(stream);
            var raw = await reader.ReadToEndAsync();
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Trace.WriteLine($"[SCHED/SVC] calendar_dates.txt: {lines.Length} lines");
            if (lines.Length > 0)
            {
                var header = lines[0].TrimStart('\uFEFF');
                Trace.WriteLine($"[SCHED/SVC] calendar_dates.txt header raw: [{header}]");
                var cols = SplitCsvLine(header);
                Trace.WriteLine($"[SCHED/SVC] calendar_dates.txt cols: [{string.Join("|", cols)}]");
                int iSvc = IndexOf(cols, "service_id");
                int iDate = IndexOf(cols, "date");
                int iType = IndexOf(cols, "exception_type");
                Trace.WriteLine($"[SCHED/SVC] calendar_dates.txt col indices: svc={iSvc} date={iDate} type={iType}");

                int matchCount = 0;
                for (int li = 1; li < lines.Length; li++)
                {
                    var line = lines[li].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    var v = SplitCsvLine(line);
                    if (iSvc < 0 || iDate < 0 || iType < 0) continue;
                    if (v.Count <= Math.Max(iSvc, Math.Max(iDate, iType))) continue;
                    if (!int.TryParse(TrimCell(v[iDate]), out int rowDate)) continue;
                    if (rowDate != dateInt) continue;
                    matchCount++;
                    var sid = TrimCell(v[iSvc]);
                    var type = TrimCell(v[iType]);
                    Trace.WriteLine($"[SCHED/SVC] calendar_dates MATCH: date={rowDate} sid={sid} type={type}");
                    if (type == "1") active.Add(sid);
                    else if (type == "2") active.Remove(sid);
                }
                Trace.WriteLine($"[SCHED/SVC] calendar_dates: {matchCount} rows matched dateInt={dateInt}");
            }
        }

        Trace.WriteLine($"[SCHED/SVC] Final active set: [{string.Join(", ", active)}]");
        return active;
    }

    private static async Task<Dictionary<string, (string RouteId, string Headsign)>>
        BuildTripInfoAsync(ZipArchive zip, HashSet<string> activeServiceIds)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var entry = zip.GetEntry("trips.txt");
        if (entry is null) { Trace.WriteLine($"[SCHED/TRIP] trips.txt NOT FOUND"); return result; }

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync();
        if (header is null) return result;
        header = header.TrimStart('\uFEFF');
        Trace.WriteLine($"[SCHED/TRIP] trips.txt header: [{header}]");

        var cols = SplitCsvLine(header);
        int iTrip = IndexOf(cols, "trip_id");
        int iRoute = IndexOf(cols, "route_id");
        int iService = IndexOf(cols, "service_id");
        int iHead = IndexOf(cols, "trip_headsign");
        Trace.WriteLine($"[SCHED/TRIP] col indices: trip={iTrip} route={iRoute} service={iService} headsign={iHead}");

        if (iTrip < 0 || iRoute < 0 || iService < 0)
        {
            Trace.WriteLine($"[SCHED/TRIP] FAIL: missing required columns");
            return result;
        }

        int total = 0, matched = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            total++;
            var v = SplitCsvLine(line);
            int need = Math.Max(iTrip, Math.Max(iRoute, iService));
            if (v.Count <= need) continue;
            var svc = TrimCell(v[iService]);
            if (!activeServiceIds.Contains(svc)) continue;
            matched++;
            var tripId = TrimCell(v[iTrip]);
            var routeId = TrimCell(v[iRoute]);
            var headsign = iHead >= 0 && iHead < v.Count ? TrimCell(v[iHead]) : "";
            if (!string.IsNullOrEmpty(tripId))
                result.TryAdd(tripId, (routeId, headsign));
        }
        Trace.WriteLine($"[SCHED/TRIP] trips.txt: {total} rows, {matched} matched active service_ids, {result.Count} unique trips");
        return result;
    }

    private sealed record RawDeparture(string RouteId, string Headsign, string DepartureTime);

    private static async Task<List<RawDeparture>> StreamStopTimesAsync(
        ZipArchive zip,
        string stopId,
        Dictionary<string, (string RouteId, string Headsign)> tripInfo)
    {
        var result = new List<RawDeparture>();
        var entry = zip.GetEntry("stop_times.txt");
        if (entry is null) { Trace.WriteLine($"[SCHED/ST] stop_times.txt NOT FOUND"); return result; }

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync();
        if (header is null) return result;
        header = header.TrimStart('\uFEFF');
        Trace.WriteLine($"[SCHED/ST] stop_times.txt header: [{header}]");

        var cols = SplitCsvLine(header);
        int iTripId = IndexOf(cols, "trip_id");
        int iStopId = IndexOf(cols, "stop_id");
        int iDepart = IndexOf(cols, "departure_time");
        if (iDepart < 0) iDepart = IndexOf(cols, "arrival_time");
        Trace.WriteLine($"[SCHED/ST] col indices: trip={iTripId} stop={iStopId} depart={iDepart}");
        Trace.WriteLine($"[SCHED/ST] Looking for stopId='{stopId}'");

        if (iTripId < 0 || iStopId < 0 || iDepart < 0)
        {
            Trace.WriteLine($"[SCHED/ST] FAIL: missing required columns in stop_times.txt");
            return result;
        }

        int maxIdx = Math.Max(iTripId, Math.Max(iStopId, iDepart));
        long rowsRead = 0, stopMatches = 0, tripMatches = 0;

        // Log first 3 raw data lines so we can see exact format
        int preview = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rowsRead++;

            if (preview < 3)
            {
                Trace.WriteLine($"[SCHED/ST] preview row {preview}: [{line}]");
                var pv = SplitCsvLine(line);
                if (pv.Count > iStopId)
                    Trace.WriteLine($"[SCHED/ST]   -> stop_id cell raw='{pv[iStopId]}' trimmed='{TrimCell(pv[iStopId])}'");
                preview++;
            }

            var v = SplitCsvLine(line);
            if (v.Count <= maxIdx) continue;

            var rowStop = TrimCell(v[iStopId]);
            if (!string.Equals(rowStop, stopId, StringComparison.OrdinalIgnoreCase)) continue;
            stopMatches++;

            var tripId = TrimCell(v[iTripId]);
            if (!tripInfo.TryGetValue(tripId, out var info))
            {
                if (stopMatches <= 5)
                    Trace.WriteLine($"[SCHED/ST] stop match but trip '{tripId}' not in active trips");
                continue;
            }
            tripMatches++;

            var depTime = TrimCell(v[iDepart]);
            if (string.IsNullOrEmpty(depTime)) continue;
            if (depTime.Length > 5 && depTime[2] == ':') depTime = depTime[..5];
            result.Add(new RawDeparture(info.RouteId, info.Headsign, depTime));
        }

        Trace.WriteLine($"[SCHED/ST] stop_times: {rowsRead} rows read, {stopMatches} stop matches, {tripMatches} trip matches, {result.Count} departures");
        return result;
    }

    private static async Task<Dictionary<string, string>> BuildRouteNameMapAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = zip.GetEntry("routes.txt");
        if (entry is null) { Trace.WriteLine("[SCHED/RT] routes.txt NOT FOUND"); return result; }
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync();
        if (header is null) return result;
        header = header.TrimStart('\uFEFF');
        var cols = SplitCsvLine(header);
        int iId = IndexOf(cols, "route_id");
        int iName = IndexOf(cols, "route_short_name");
        if (iId < 0) { Trace.WriteLine("[SCHED/RT] routes.txt missing route_id column"); return result; }
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = SplitCsvLine(line);
            if (v.Count <= iId) continue;
            var id = TrimCell(v[iId]);
            var name = iName >= 0 && iName < v.Count ? TrimCell(v[iName]) : id;
            if (!string.IsNullOrEmpty(id)) result.TryAdd(id, name);
        }
        Trace.WriteLine($"[SCHED/RT] routes loaded: {result.Count}");
        return result;
    }

    // ── Add these two helpers if not already in GtfsService ──────────────────


    // ── Add to GtfsService.cs ─────────────────────────────────────────────────
    // 
    // 1. Call BuildStopRouteTypeMapAsync(op.Id) at the end of InstallAsync,
    //    right after BuildTripRouteMapAsync(op.Id).
    //
    // 2. In ParseStopsAsync, after building the stop list, call
    //    ApplyStopRouteTypes(operatorId, stops) before returning.
    //
    // 3. Add RouteType = 3 property to GtfsStopDto (default bus).

    // ────────────────────────────────────────────────────────────────────
    // Stop → RouteType map  (built once at install, cached to disk)
    // ────────────────────────────────────────────────────────────────────

    private const string StopRouteTypeFile = "stop_route_types.json";

    /// <summary>
    /// Scans stop_times.txt + trips.txt + routes.txt once at install time.
    /// For each stop, records the lowest route_type that serves it
    /// (tram=0 wins over bus=3). Result cached to a small JSON file.
    /// </summary>
    public async Task BuildStopRouteTypeMapAsync(int operatorId)
    {
        using var zip = OpenZip(operatorId);
        if (zip is null) return;

        // 1. route_id → route_type  (from routes.txt — tiny file)
        var routeTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var routeEntry = zip.GetEntry("routes.txt");
        if (routeEntry is not null)
        {
            await using var rs = routeEntry.Open();
            using var rr = new StreamReader(rs);
            var hdr = (await rr.ReadLineAsync() ?? "").TrimStart('\uFEFF');
            var cols = SplitCsvLine(hdr);
            int iId = IndexOf(cols, "route_id"), iType = IndexOf(cols, "route_type");
            if (iId >= 0 && iType >= 0)
            {
                string? line;
                while ((line = await rr.ReadLineAsync()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var v = SplitCsvLine(line);
                    if (v.Count <= Math.Max(iId, iType)) continue;
                    var rid = TrimCell(v[iId]);
                    if (int.TryParse(TrimCell(v[iType]), out int rt) && !string.IsNullOrEmpty(rid))
                        routeTypes.TryAdd(rid, rt);
                }
            }
        }

        // 2. trip_id → route_type  (from trips.txt — medium file, ~93k rows)
        var tripTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var tripEntry = zip.GetEntry("trips.txt");
        if (tripEntry is not null)
        {
            await using var ts = tripEntry.Open();
            using var tr = new StreamReader(ts);
            var hdr = (await tr.ReadLineAsync() ?? "").TrimStart('\uFEFF');
            var cols = SplitCsvLine(hdr);
            int iTrip = IndexOf(cols, "trip_id"), iRoute = IndexOf(cols, "route_id");
            if (iTrip >= 0 && iRoute >= 0)
            {
                string? line;
                while ((line = await tr.ReadLineAsync()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var v = SplitCsvLine(line);
                    if (v.Count <= Math.Max(iTrip, iRoute)) continue;
                    var tid = TrimCell(v[iTrip]);
                    var rid = TrimCell(v[iRoute]);
                    if (!string.IsNullOrEmpty(tid) && routeTypes.TryGetValue(rid, out int rt))
                        tripTypes.TryAdd(tid, rt);
                }
            }
        }

        // 3. Stream stop_times.txt → build stopId → min(routeType)
        //    tram (0) beats bus (3) so each stop gets the most "specific" type
        var stopTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var stEntry = zip.GetEntry("stop_times.txt");
        if (stEntry is not null)
        {
            await using var sts = stEntry.Open();
            using var str = new StreamReader(sts);
            var hdr = (await str.ReadLineAsync() ?? "").TrimStart('\uFEFF');
            var cols = SplitCsvLine(hdr);
            int iTrip = IndexOf(cols, "trip_id"), iStop = IndexOf(cols, "stop_id");
            if (iTrip >= 0 && iStop >= 0)
            {
                int maxIdx = Math.Max(iTrip, iStop);
                string? line;
                while ((line = await str.ReadLineAsync()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var v = SplitCsvLine(line);
                    if (v.Count <= maxIdx) continue;
                    var sid = TrimCell(v[iStop]);
                    var tid = TrimCell(v[iTrip]);
                    if (string.IsNullOrEmpty(sid) || !tripTypes.TryGetValue(tid, out int rt)) continue;
                    // Keep the lowest (most specific) route type per stop
                    if (!stopTypes.TryGetValue(sid, out int existing) || rt < existing)
                        stopTypes[sid] = rt;
                }
            }
        }

        // 4. Save to disk next to the zip
        var path = Path.Combine(GetOperatorDir(operatorId), StopRouteTypeFile);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(stopTypes));
        Trace.WriteLine($"[GtfsService] Built stop→routeType map: {stopTypes.Count} stops → {path}");
    }

    /// <summary>
    /// Reads the cached stop→routeType map and applies it to a list of stops.
    /// Call this at the end of ParseStopsAsync before returning.
    /// </summary>
    public void ApplyStopRouteTypes(int operatorId, List<GtfsStopDto> stops)
    {
        var path = Path.Combine(GetOperatorDir(operatorId), StopRouteTypeFile);
        if (!File.Exists(path))
        {
            Trace.WriteLine($"[GtfsService] stop_route_types.json not found at {path}");
            return;
        }
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            if (map is null) { Trace.WriteLine("[GtfsService] stop_route_types.json is null"); return; }
            int applied = 0;
            foreach (var s in stops)
                if (map.TryGetValue(s.StopId, out int rt))
                { s.RouteType = rt; applied++; }
            int trams = stops.Count(s => s.RouteType == 0);
            Trace.WriteLine($"[GtfsService] ApplyStopRouteTypes: {map.Count} entries, {applied}/{stops.Count} applied, {trams} tram stops");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[GtfsService] Failed to load stop route types: {ex.Message}");
        }
    }


    // ── Also add these two helpers if not already present ─────────────────────

    private static int IndexOf(List<string> cols, string name)
    {
        for (int i = 0; i < cols.Count; i++)
            if (string.Equals(cols[i].Trim().Trim('"').Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static string TrimCell(string s) => s.Trim().Trim('"').Trim();
}