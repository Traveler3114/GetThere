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
        }).Where(s => s.Lat != 0 && s.Lon != 0).ToList();
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
}