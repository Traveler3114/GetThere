using GetThereShared.Dtos;
using System.IO.Compression;
using System.Text.Json;
using System.Diagnostics;

namespace GetThere.Services;

public class GtfsService
{
    private const string InstalledKey = "installed_operator_ids";
    private const string RealtimeUrlsKey = "realtime_feed_urls";

    private readonly HttpClient _http;

    public GtfsService(HttpClient http) => _http = http;

    // ────────────────────────────
    // Install / Remove
    // ────────────────────────────

    public async Task<bool> InstallAsync(TransitOperatorDto op, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(op.GtfsFeedUrl)) return false;
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
            if (total > 0) progress?.Report((double)downloaded / total);
        }
        MarkInstalled(op.Id);
        if (!string.IsNullOrEmpty(op.GtfsRealtimeFeedUrl))
            SaveRealtimeUrl(op.Id, op.GtfsRealtimeFeedUrl);
        return true;
    }

    public void Remove(int operatorId)
    {
        var dir = GetOperatorDir(operatorId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        MarkRemoved(operatorId);
        RemoveRealtimeUrl(operatorId);
    }

    public bool IsInstalled(int operatorId) => GetInstalledIds().Contains(operatorId);
    public bool HasRealtime(int operatorId) => !string.IsNullOrEmpty(GetRealtimeUrl(operatorId));

    public string GetOperatorDir(int operatorId)
        => Path.Combine(FileSystem.AppDataDirectory, "gtfs", operatorId.ToString());

    public long GetSizeBytes(int operatorId)
    {
        var zip = Path.Combine(GetOperatorDir(operatorId), "gtfs.zip");
        return File.Exists(zip) ? new FileInfo(zip).Length : 0;
    }

    // ────────────────────────────
    // Parse Stops / Routes
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
            if (routeShapeMap.TryGetValue(route.RouteId, out var shapeId)
                && shapes.TryGetValue(shapeId, out var coords))
                route.Shape = coords;
        return routes;
    }

    // ────────────────────────────
    // Realtime Vehicles — pure manual protobuf parser, no library needed
    // ────────────────────────────

    public async Task<List<VehiclePositionDto>> GetVehiclesAsync(int operatorId)
    {
        var feedUrl = GetRealtimeUrl(operatorId);
        if (string.IsNullOrEmpty(feedUrl)) return [];

        var data = await _http.GetByteArrayAsync(feedUrl);
        Trace.WriteLine($"[Realtime] Fetched {data.Length} bytes");

        var result = new List<VehiclePositionDto>();
        int pos = 0;
        int entityCount = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (pos >= data.Length && fieldNum == 0) break;

            if (fieldNum == 2 && wireType == 2) // FeedEntity
            {
                entityCount++;
                var (entityBytes, p2) = ReadLengthDelimited(data, pos);
                pos = p2;

                var dto = ParseEntity(entityBytes);
                if (dto != null) result.Add(dto);
            }
            else
            {
                pos = SkipField(data, pos, wireType);
            }
        }
        if (result.Count > 0)
        {
            var sample = string.Join(", ", result.Take(5).Select(v => $"{v.VehicleId}→r{v.RouteId}@{v.Lat:F4},{v.Lon:F4}"));
            Trace.WriteLine($"[Realtime] Parsed {result.Count} vehicles. Sample: {sample}");
        }
        else
        {
            Trace.WriteLine($"[Realtime] Parsed 0 vehicles from {entityCount} entities");
        }

        return result;
    }

    private static VehiclePositionDto? ParseEntity(byte[] data)
    {
        int pos = 0;
        string entityId = string.Empty;
        VehiclePositionDto? dto = null;

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (fieldNum == 1 && wireType == 2) // entity id
            {
                var (bytes, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                entityId = System.Text.Encoding.UTF8.GetString(bytes);
            }
            else if (fieldNum == 4 && wireType == 2) // VehiclePosition = field 4 (standard GTFS-RT)
            {
                var (vpBytes, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                dto = ParseVehiclePosition(vpBytes);
            }
            else
            {
                pos = SkipField(data, pos, wireType);
            }
        }

        if (dto == null || (dto.Lat == 0 && dto.Lon == 0)) return null;
        if (string.IsNullOrEmpty(dto.VehicleId)) dto.VehicleId = entityId;
        return dto;
    }

    private static VehiclePositionDto ParseVehiclePosition(byte[] data)
    {
        var dto = new VehiclePositionDto();
        int pos = 0;

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                if (fieldNum == 1) ParseTrip(sub, dto);
                else if (fieldNum == 2) ParsePosition(sub, dto);
                else if (fieldNum == 3) ParseVehicleDescriptor(sub, dto);
            }
            else
            {
                pos = SkipField(data, pos, wireType);
            }
        }
        return dto;
    }

    private static void ParseTrip(byte[] data, VehiclePositionDto dto)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;
            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                var str = System.Text.Encoding.UTF8.GetString(sub);
                if (fieldNum == 1) // trip_id format: "0_<serviceId>_<blockId>_<routeId>_<seq>"
                {
                    dto.VehicleId = str; // use trip_id as fallback vehicle id
                    var parts = str.Split('_');
                    if (parts.Length >= 4 && string.IsNullOrEmpty(dto.RouteId))
                        dto.RouteId = parts[3]; // route_id is at index 3
                }
                else if (fieldNum == 2) dto.RouteId = str; // explicit route_id overrides
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    private static void ParsePosition(byte[] data, VehiclePositionDto dto)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;
            if (wireType == 5 && pos + 4 <= data.Length) // fixed32 = float
            {
                float val = BitConverter.ToSingle(data, pos);
                pos += 4;
                if (fieldNum == 1) dto.Lat = val;
                else if (fieldNum == 2) dto.Lon = val;
                else if (fieldNum == 3) dto.Bearing = val;
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    private static void ParseVehicleDescriptor(byte[] data, VehiclePositionDto dto)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;
            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                var str = System.Text.Encoding.UTF8.GetString(sub);
                if (fieldNum == 1) dto.VehicleId = str;
                else if (fieldNum == 2) dto.Label = str;
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    // ────────────────────────────
    // Protobuf primitives
    // ────────────────────────────

    private static (int field, int wire, int pos) ReadTag(byte[] data, int pos)
    {
        if (pos >= data.Length) return (0, 0, pos);
        var (varint, newPos) = ReadVarint(data, pos);
        return ((int)(varint >> 3), (int)(varint & 7), newPos);
    }

    private static (ulong value, int pos) ReadVarint(byte[] data, int pos)
    {
        ulong result = 0;
        int shift = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return (result, pos);
    }

    private static (byte[] bytes, int pos) ReadLengthDelimited(byte[] data, int pos)
    {
        var (length, newPos) = ReadVarint(data, pos);
        int len = (int)length;
        var bytes = new byte[len];
        Array.Copy(data, newPos, bytes, 0, Math.Min(len, data.Length - newPos));
        return (bytes, newPos + len);
    }

    private static int SkipField(byte[] data, int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: // varint
                while (pos < data.Length && (data[pos++] & 0x80) != 0) { }
                return pos;
            case 1: return pos + 8;  // 64-bit
            case 2:                  // length-delimited
                var (len, newPos) = ReadVarint(data, pos);
                return newPos + (int)len;
            case 5: return pos + 4;  // 32-bit
            default: return data.Length; // unknown, bail
        }
    }

    // ────────────────────────────
    // Realtime URL cache
    // ────────────────────────────

    private void SaveRealtimeUrl(int operatorId, string url)
    {
        var map = GetRealtimeUrlMap();
        map[operatorId] = url;
        Preferences.Set(RealtimeUrlsKey, JsonSerializer.Serialize(map));
    }
    private void RemoveRealtimeUrl(int operatorId)
    {
        var map = GetRealtimeUrlMap();
        map.Remove(operatorId);
        Preferences.Set(RealtimeUrlsKey, JsonSerializer.Serialize(map));
    }
    private string? GetRealtimeUrl(int operatorId)
    {
        var map = GetRealtimeUrlMap();
        return map.TryGetValue(operatorId, out var url) ? url : null;
    }
    private Dictionary<int, string> GetRealtimeUrlMap()
    {
        var json = Preferences.Get(RealtimeUrlsKey, "{}");
        return JsonSerializer.Deserialize<Dictionary<int, string>>(json) ?? [];
    }

    // ────────────────────────────
    // Installed IDs
    // ────────────────────────────

    private HashSet<int> GetInstalledIds()
    {
        var json = Preferences.Get(InstalledKey, "[]");
        return JsonSerializer.Deserialize<HashSet<int>>(json) ?? [];
    }
    private void MarkInstalled(int id) { var s = GetInstalledIds(); s.Add(id); Preferences.Set(InstalledKey, JsonSerializer.Serialize(s)); }
    private void MarkRemoved(int id) { var s = GetInstalledIds(); s.Remove(id); Preferences.Set(InstalledKey, JsonSerializer.Serialize(s)); }

    // ────────────────────────────
    // Zip / CSV helpers
    // ────────────────────────────

    private ZipArchive? OpenZip(int operatorId)
    {
        var path = Path.Combine(GetOperatorDir(operatorId), "gtfs.zip");
        return File.Exists(path) ? ZipFile.OpenRead(path) : null;
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