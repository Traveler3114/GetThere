//using Android.AdServices.Topics;
using GetThere.Helpers;
using GetThere.Services.Realtime;
using GetThereShared.Dtos;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Google.Protobuf;

namespace GetThere.Services;

/// <summary>
/// Handles GTFS static data (install, remove, parse stops/routes/schedule) and
/// realtime vehicle polling for any operator.
/// </summary>
public class GtfsService
{
    private const string InstalledKey = "installed_operator_ids";
    private const string RealtimeUrlsKey = "realtime_feed_urls";
    private const string TripMapKeyPrefix = "trip_route_map_";
    private const string StopRouteTypeFile = "stop_route_types.json";

    // In-memory cache for trip stop sequences — avoids re-scanning 100MB stop_times.txt
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<TripStopDto>>
        _tripStopCache = new(StringComparer.Ordinal);

    private static readonly SemaphoreSlim _zipLock = new(1, 1);

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
        finally { _zipLock.Release(); }

            SaveOperatorConfig(op);

        await BuildTripRouteMapAsync(op.Id);
        await BuildStopRouteTypeMapAsync(op.Id);

        return true;
    }

    public void Remove(int operatorId)
    {
        // Mark as removed first so no new reads start
        MarkRemoved(operatorId);
        RemoveOperatorConfig(operatorId);
        Preferences.Remove($"{TripMapKeyPrefix}{operatorId}");

        // Wait for any in-progress zip reads to finish, then delete
        _zipLock.Wait();
        try
        {
            var dir = GetOperatorDir(operatorId);
            if (Directory.Exists(dir))
            {
                // Retry loop — zip may still be open by a concurrent reader
                for (int i = 0; i < 5; i++)
                {
                    try { Directory.Delete(dir, recursive: true); break; }
                    catch (IOException) when (i < 4)
                    {
                        System.Threading.Thread.Sleep(200);
                    }
                }
            }
        }
        finally { _zipLock.Release(); }
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

    /// <summary>Public accessor for external zip readers (e.g. realtime parsers).</summary>
    public ZipArchive? OpenZipPublic(int operatorId) => OpenZip(operatorId);

    // ────────────────────────────────────────────────────────────────────
    // Trip → Route map
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

    /// <summary>Public accessor for trip→route map (for operator resolution in MapPage).</summary>
    public Dictionary<string, string>? GetTripRouteMapPublic(int operatorId) => GetTripRouteMap(operatorId);

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

        if (op.RealtimeAuthType == "API_KEY_QUERY" && !string.IsNullOrEmpty(op.RealtimeAuthConfig))
        {
            var parts = op.RealtimeAuthConfig.Split(':', 2);
            if (parts.Length == 2)
                url += (url.Contains('?') ? "&" : "?") + $"{parts[0]}={Uri.EscapeDataString(parts[1])}";
        }

        var msg = new HttpRequestMessage(HttpMethod.Get, url);

        if ((op.RealtimeAuthType == "API_KEY_HEADER" || op.RealtimeAuthType == "BEARER")
            && !string.IsNullOrEmpty(op.RealtimeAuthConfig))
        {
            var parts = op.RealtimeAuthConfig.Split(':', 2);
            if (parts.Length == 2) msg.Headers.TryAddWithoutValidation(parts[0], parts[1]);
        }

        return msg;
    }



    // ────────────────────────────────────────────────────────────────────
    // Parse STOP ŠULDES
    // ────────────────────────────────────────────────────────────────────

    public async Task<List<GtfsStopSchedule>> ParseStopTimesAsync(int operatorId)
    {
        using var zip = OpenZip(operatorId);
        var entry = zip?.GetEntry("stop_times.txt");
        if (entry is null) return [];
        await using var stream = entry.Open();
        var rows = await ParseCsvAsync(stream);
        return rows.Select(r => new GtfsStopSchedule
        {
            TripId = Get(r, "trip_id"),
            ArrivalTime = Get(r, "arrival_time"),
            DepartureTime = Get(r, "departure_time"),
            Destination = Get(r, "stop_headsign"),
            StopId = Get(r, "stop_id"),
        }).ToList();
    }



    // ────────────────────────────────────────────────────────────────────
    // Parse Stops
    // ────────────────────────────────────────────────────────────────────

    public async Task<List<GtfsStopDto>> ParseStopsAsync(int operatorId)
    {
        using var zip = OpenZip(operatorId);
        var entry = zip?.GetEntry("stops.txt");
        if (entry is null) return [];
        await using var stream = entry.Open();
        var rows = await ParseCsvAsync(stream);
        return rows.Select(r => new GtfsStopDto
        {
            StopId = Get(r, "stop_id"),
            Name = Get(r, "stop_name"),
            Lat = ParseDouble(Get(r, "stop_lat")),
            Lon = ParseDouble(Get(r, "stop_lon")),
        })
        .Where(s => s.StopId.Contains('_'))
        .ToList();
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
            Color = string.IsNullOrWhiteSpace(Get(r, "route_color")) ? "1a73e8" : Get(r, "route_color"),
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
    // Stop Schedule  (streams stop_times.txt — safe for 100 MB files)
    // ────────────────────────────────────────────────────────────────────

    public async Task<List<StopDepartureGroupDto>> ParseStopScheduleAsync(
        int operatorId,
        string stopId,
        DateOnly date)
    {
        using var zip = OpenZip(operatorId);
        if (zip is null) return [];

        var activeServiceIds = await GetActiveServiceIdsAsync(zip, date);
        if (activeServiceIds.Count == 0) return [];

        var tripInfo = await BuildTripInfoAsync(zip, activeServiceIds);
        if (tripInfo.Count == 0) return [];

        var departures = await StreamStopTimesAsync(zip, stopId, tripInfo);
        var routeNames = await BuildRouteNameMapAsync(zip);

        // Filter past departures — 1-min grace so "leaving now" still shows
        int nowMins = DateTime.Now.Hour * 60 + DateTime.Now.Minute - 1;

        return departures
            .GroupBy(d => (d.RouteId, d.Headsign))
            .Select(g =>
            {
                var deps = g
                    .Select(d => new StopDepartureDto
                    {
                        ScheduledTime = d.DepartureTime,
                        TripId = d.TripId,
                    })
                    .Where(d => TimeToMinutes(d.ScheduledTime) >= nowMins)
                    .OrderBy(d => TimeToMinutes(d.ScheduledTime))
                    .GroupBy(d => d.ScheduledTime).Select(tg => tg.First()) // deduplicate
                    .ToList();

                if (deps.Count == 0) return null;

                return new StopDepartureGroupDto
                {
                    RouteId = g.Key.RouteId,
                    ShortName = routeNames.TryGetValue(g.Key.RouteId, out var n) ? n : g.Key.RouteId,
                    Headsign = g.Key.Headsign,
                    Departures = deps,
                };
            })
            .Where(g => g != null)
            .Cast<StopDepartureGroupDto>()
            .OrderBy(g => TimeToMinutes(g.Departures.First().ScheduledTime))
            .ToList();
    }

    private static int TimeToMinutes(string t)
    {
        var p = t.Split(':');
        return p.Length >= 2 && int.TryParse(p[0], out int h) && int.TryParse(p[1], out int m)
            ? h * 60 + m : 0;
    }

    private static async Task<HashSet<string>> GetActiveServiceIdsAsync(ZipArchive zip, DateOnly date)
    {
        int dateInt = date.Year * 10000 + date.Month * 100 + date.Day;
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

        var active = new HashSet<string>(StringComparer.Ordinal);

        var calEntry = zip.GetEntry("calendar.txt");
        if (calEntry is not null)
        {
            await using var stream = calEntry.Open();
            using var reader = new StreamReader(stream);
            var raw = await reader.ReadToEndAsync();
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                var cols = SplitCsvLine(lines[0].TrimStart('\uFEFF'));
                int iSvc = IndexOf(cols, "service_id");
                int iStart = IndexOf(cols, "start_date");
                int iEnd = IndexOf(cols, "end_date");
                int iDay = IndexOf(cols, dayCol);
                if (iSvc >= 0 && iStart >= 0 && iEnd >= 0 && iDay >= 0)
                    for (int li = 1; li < lines.Length; li++)
                    {
                        var v = SplitCsvLine(lines[li].Trim());
                        if (v.Count <= Math.Max(iSvc, Math.Max(iStart, Math.Max(iEnd, iDay)))) continue;
                        if (!int.TryParse(TrimCell(v[iStart]), out int s)) continue;
                        if (!int.TryParse(TrimCell(v[iEnd]), out int e)) continue;
                        if (dateInt >= s && dateInt <= e && TrimCell(v[iDay]) == "1")
                            active.Add(TrimCell(v[iSvc]));
                    }
            }
        }

        var datesEntry = zip.GetEntry("calendar_dates.txt");
        if (datesEntry is not null)
        {
            await using var stream = datesEntry.Open();
            using var reader = new StreamReader(stream);
            var raw = await reader.ReadToEndAsync();
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                var cols = SplitCsvLine(lines[0].TrimStart('\uFEFF'));
                int iSvc = IndexOf(cols, "service_id");
                int iDate = IndexOf(cols, "date");
                int iType = IndexOf(cols, "exception_type");
                if (iSvc >= 0 && iDate >= 0 && iType >= 0)
                    for (int li = 1; li < lines.Length; li++)
                    {
                        var v = SplitCsvLine(lines[li].Trim());
                        if (v.Count <= Math.Max(iSvc, Math.Max(iDate, iType))) continue;
                        if (!int.TryParse(TrimCell(v[iDate]), out int rowDate) || rowDate != dateInt) continue;
                        var sid = TrimCell(v[iSvc]);
                        var type = TrimCell(v[iType]);
                        if (type == "1") active.Add(sid);
                        else if (type == "2") active.Remove(sid);
                    }
            }
        }

        return active;
    }

    private static async Task<Dictionary<string, (string RouteId, string Headsign)>>
        BuildTripInfoAsync(ZipArchive zip, HashSet<string> activeServiceIds)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var entry = zip.GetEntry("trips.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var header = (await reader.ReadLineAsync() ?? "").TrimStart('\uFEFF');
        var cols = SplitCsvLine(header);
        int iTrip = IndexOf(cols, "trip_id");
        int iRoute = IndexOf(cols, "route_id");
        int iService = IndexOf(cols, "service_id");
        int iHead = IndexOf(cols, "trip_headsign");
        if (iTrip < 0 || iRoute < 0 || iService < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = SplitCsvLine(line);
            if (v.Count <= Math.Max(iTrip, Math.Max(iRoute, iService))) continue;
            if (!activeServiceIds.Contains(TrimCell(v[iService]))) continue;
            var tripId = TrimCell(v[iTrip]);
            var routeId = TrimCell(v[iRoute]);
            var headsign = iHead >= 0 && iHead < v.Count ? TrimCell(v[iHead]) : "";
            if (!string.IsNullOrEmpty(tripId))
                result.TryAdd(tripId, (routeId, headsign));
        }
        return result;
    }

    private sealed record RawDeparture(string RouteId, string Headsign, string TripId, string DepartureTime);

    private static async Task<List<RawDeparture>> StreamStopTimesAsync(
        ZipArchive zip,
        string stopId,
        Dictionary<string, (string RouteId, string Headsign)> tripInfo)
    {
        var result = new List<RawDeparture>();
        var entry = zip.GetEntry("stop_times.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var header = (await reader.ReadLineAsync() ?? "").TrimStart('\uFEFF');
        var cols = SplitCsvLine(header);
        int iTripId = IndexOf(cols, "trip_id");
        int iStopId = IndexOf(cols, "stop_id");
        int iDepart = IndexOf(cols, "departure_time");
        if (iDepart < 0) iDepart = IndexOf(cols, "arrival_time");
        if (iTripId < 0 || iStopId < 0 || iDepart < 0) return result;

        int maxIdx = Math.Max(iTripId, Math.Max(iStopId, iDepart));
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = SplitCsvLine(line);
            if (v.Count <= maxIdx) continue;
            if (!string.Equals(TrimCell(v[iStopId]), stopId, StringComparison.OrdinalIgnoreCase)) continue;
            var tripId = TrimCell(v[iTripId]);
            if (!tripInfo.TryGetValue(tripId, out var info)) continue;
            var depTime = TrimCell(v[iDepart]);
            if (string.IsNullOrEmpty(depTime)) continue;
            if (depTime.Length > 5 && depTime[2] == ':') depTime = depTime[..5];
            result.Add(new RawDeparture(info.RouteId, info.Headsign, tripId, depTime));
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> BuildRouteNameMapAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = zip.GetEntry("routes.txt");
        if (entry is null) return result;
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var header = (await reader.ReadLineAsync() ?? "").TrimStart('\uFEFF');
        var cols = SplitCsvLine(header);
        int iId = IndexOf(cols, "route_id");
        int iName = IndexOf(cols, "route_short_name");
        if (iId < 0) return result;
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
        return result;
    }

    // ────────────────────────────────────────────────────────────────────
    // Stop → RouteType cache  (built once at install, cached to disk)
    // ────────────────────────────────────────────────────────────────────

    public async Task BuildStopRouteTypeMapAsync(int operatorId)
    {
        using var zip = OpenZip(operatorId);
        if (zip is null) return;

        // route_id → route_type
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

        // trip_id → route_type
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

        // stop_id → min(route_type)  — tram(0) beats bus(3)
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
                    if (!stopTypes.TryGetValue(sid, out int existing) || rt < existing)
                        stopTypes[sid] = rt;
                }
            }
        }

        var path = Path.Combine(GetOperatorDir(operatorId), StopRouteTypeFile);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(stopTypes));
        Trace.WriteLine($"[GtfsService] Built stop→routeType map: {stopTypes.Count} stops");
    }

    public void ApplyStopRouteTypes(int operatorId, List<GtfsStopDto> stops)
    {
        var path = Path.Combine(GetOperatorDir(operatorId), StopRouteTypeFile);
        if (!File.Exists(path)) return;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            if (map is null) return;
            int applied = 0;
            foreach (var s in stops)
                if (map.TryGetValue(s.StopId, out int rt)) { s.RouteType = rt; applied++; }
            Trace.WriteLine($"[GtfsService] ApplyStopRouteTypes: {applied}/{stops.Count} applied");
        }
        catch (Exception ex) { Trace.WriteLine($"[GtfsService] stop route types error: {ex.Message}"); }
    }

    // ────────────────────────────────────────────────────────────────────
    // Operator config cache
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
        try { return JsonSerializer.Deserialize<Dictionary<int, OperatorRealtimeConfig>>(json) ?? []; }
        catch (JsonException)
        {
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
            // Wait for any in-progress install to finish writing
            _zipLock.Wait();
            _zipLock.Release();
            // Open with FileShare.ReadWrite|Delete so Remove() can proceed
            // even while this archive is open
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            return new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
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

    private static int IndexOf(List<string> cols, string name)
    {
        for (int i = 0; i < cols.Count; i++)
            if (string.Equals(cols[i].Trim().Trim('"').Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static string TrimCell(string s) => s.Trim().Trim('"').Trim();

    // ────────────────────────────────────────────────────────────────────
    // Trip Detail  (full stop sequence for a trip)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full ordered stop sequence for <paramref name="tripId"/>,
    /// with stop names and scheduled times.
    /// Streams stop_times.txt line-by-line (safe for 100 MB files).
    /// </summary>
    public async Task<List<TripStopDto>> ParseTripStopsAsync(int operatorId, string tripId)
    {
        // Return cached result if available — avoids re-scanning 100MB stop_times.txt
        if (_tripStopCache.TryGetValue(tripId, out var cached))
            return cached;

        using var zip = OpenZip(operatorId);
        if (zip is null) return [];

        // 1. Load stop coords + names into a quick lookup
        var stopMeta = new Dictionary<string, (string Name, double Lat, double Lon)>(StringComparer.Ordinal);
        var stopsEntry = zip.GetEntry("stops.txt");
        if (stopsEntry is not null)
        {
            await using var ss = stopsEntry.Open();
            using var sr = new StreamReader(ss);
            var hdr = (await sr.ReadLineAsync() ?? "").TrimStart('\uFEFF');
            var cols = SplitCsvLine(hdr);
            int iId = IndexOf(cols, "stop_id");
            int iNm = IndexOf(cols, "stop_name");
            int iLat = IndexOf(cols, "stop_lat");
            int iLon = IndexOf(cols, "stop_lon");
            if (iId >= 0 && iNm >= 0)
            {
                string? line;
                while ((line = await sr.ReadLineAsync()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var v = SplitCsvLine(line);
                    int need = Math.Max(iId, Math.Max(iNm, Math.Max(iLat < 0 ? 0 : iLat, iLon < 0 ? 0 : iLon)));
                    if (v.Count <= need) continue;
                    var sid = TrimCell(v[iId]);
                    var name = TrimCell(v[iNm]);
                    var lat = iLat >= 0 && iLat < v.Count ? ParseDouble(TrimCell(v[iLat])) : 0;
                    var lon = iLon >= 0 && iLon < v.Count ? ParseDouble(TrimCell(v[iLon])) : 0;
                    if (!string.IsNullOrEmpty(sid))
                        stopMeta.TryAdd(sid, (name, lat, lon));
                }
            }
        }

        // 2. Stream stop_times.txt — collect only rows for this trip
        var result = new List<TripStopDto>();
        var stEntry = zip.GetEntry("stop_times.txt");
        if (stEntry is null) return result;

        await using var sts = stEntry.Open();
        using var stReader = new StreamReader(sts);
        var stHdr = (await stReader.ReadLineAsync() ?? "").TrimStart('\uFEFF');
        var stCols = SplitCsvLine(stHdr);
        int iTripId = IndexOf(stCols, "trip_id");
        int iStopId = IndexOf(stCols, "stop_id");
        int iSeq = IndexOf(stCols, "stop_sequence");
        int iDep = IndexOf(stCols, "departure_time");
        if (iDep < 0) iDep = IndexOf(stCols, "arrival_time");

        if (iTripId < 0 || iStopId < 0 || iSeq < 0 || iDep < 0) return result;
        int maxIdx = Math.Max(iTripId, Math.Max(iStopId, Math.Max(iSeq, iDep)));

        string? stLine;
        while ((stLine = await stReader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(stLine)) continue;
            var v = SplitCsvLine(stLine);
            if (v.Count <= maxIdx) continue;
            if (!string.Equals(TrimCell(v[iTripId]), tripId, StringComparison.OrdinalIgnoreCase)) continue;

            var sid = TrimCell(v[iStopId]);
            var depTime = TrimCell(v[iDep]);
            if (depTime.Length > 5 && depTime[2] == ':') depTime = depTime[..5];
            int seq = int.TryParse(TrimCell(v[iSeq]), out int s) ? s : 0;

            stopMeta.TryGetValue(sid, out var meta);
            result.Add(new TripStopDto
            {
                Sequence = seq,
                StopId = sid,
                StopName = meta.Name ?? sid,
                Lat = meta.Lat,
                Lon = meta.Lon,
                ScheduledTime = depTime,
            });
        }

        result.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

        // Cache for subsequent calls (cleared on Remove/reinstall)
        _tripStopCache.TryAdd(tripId, result);
        Trace.WriteLine($"[GtfsService] Cached {result.Count} stops for trip {tripId} (cache size: {_tripStopCache.Count})");

        return result;
    }

    public void ClearTripStopCache() => _tripStopCache.Clear();
}