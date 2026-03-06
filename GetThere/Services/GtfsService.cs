using GetThereShared.Dtos;
using System.IO.Compression;
using System.Text.Json;
using System.Net.Http.Json;

namespace GetThere.Services;

public class GtfsService
{
    private const string InstalledKey = "installed_operator_ids";
    private readonly HttpClient _http;

    public GtfsService(HttpClient http) => _http = http;

    // ────────────────────────────
    // Download/Remove GTFS Static
    // ────────────────────────────

    public async Task<bool> InstallAsync(TransitOperatorDto op, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(op.GtfsFeedUrl))
            return false;
        var dir = GetOperatorDir(op.Id);
        var zipPath = Path.Combine(dir, "gtfs.zip");
        Directory.CreateDirectory(dir);

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
            if (total > 0)
                progress?.Report((double)downloaded / total);
        }
        MarkInstalled(op.Id);
        return true;
    }

    public void Remove(int operatorId)
    {
        var dir = GetOperatorDir(operatorId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        MarkRemoved(operatorId);
    }

    public bool IsInstalled(int operatorId)
        => GetInstalledIds().Contains(operatorId);

    public string GetOperatorDir(int operatorId)
        => Path.Combine(FileSystem.AppDataDirectory, "gtfs", operatorId.ToString());

    public long GetSizeBytes(int operatorId)
    {
        var zip = Path.Combine(GetOperatorDir(operatorId), "gtfs.zip");
        return File.Exists(zip) ? new FileInfo(zip).Length : 0;
    }

    // ────────────────────────────
    // Parse Stops/Routes from Local GTFS
    // ────────────────────────────

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
        {
            if (routeShapeMap.TryGetValue(route.RouteId, out var shapeId)
                && shapes.TryGetValue(shapeId, out var coords))
            {
                route.Shape = coords;
            }
        }
        return routes;
    }

    // ────────────────────────────
    // Fetch Realtime Vehicles (Proxy API)
    // ────────────────────────────

    public async Task<List<VehiclePositionDto>> GetVehiclesAsync(int operatorId)
    {
        var result = await _http
            .GetFromJsonAsync<OperationResult<List<VehiclePositionDto>>>(
                $"gtfs/{operatorId}/realtime/vehicles");
        return result?.Data ?? [];
    }

    // ────────────────────────────
    // Helpers
    // ────────────────────────────

    private ZipArchive? OpenZip(int operatorId)
    {
        var path = Path.Combine(GetOperatorDir(operatorId), "gtfs.zip");
        return File.Exists(path) ? ZipFile.OpenRead(path) : null;
    }

    private HashSet<int> GetInstalledIds()
    {
        var json = Preferences.Get(InstalledKey, "[]");
        return JsonSerializer.Deserialize<HashSet<int>>(json) ?? [];
    }
    private void MarkInstalled(int id)
    {
        var set = GetInstalledIds();
        set.Add(id);
        Preferences.Set(InstalledKey, JsonSerializer.Serialize(set));
    }
    private void MarkRemoved(int id)
    {
        var set = GetInstalledIds();
        set.Remove(id);
        Preferences.Set(InstalledKey, JsonSerializer.Serialize(set));
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
        => double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
}