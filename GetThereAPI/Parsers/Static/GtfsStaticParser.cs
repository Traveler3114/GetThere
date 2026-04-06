using GetThereShared.Dtos;
using System.IO.Compression;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Parses GTFS static feeds — ZIP files containing CSV text files.
///
/// Handles common real-world deviations from the spec:
///   - BOM characters at start of files
///   - Quoted fields containing commas
///   - Missing optional columns (location_type, shape_id etc.)
///   - Times past midnight e.g. "25:30:00" for after-midnight trips
/// </summary>
public class GtfsStaticParser : IStaticDataParser
{
    // ── Public interface methods ──────────────────────────────────────────

    public async Task<List<StopDto>> ParseStopsAsync(byte[] data)
    {
        using var zip = Open(data);

        var routeTypes   = await ParseRouteTypesAsync(zip);
        var tripRouteMap = await ParseTripRouteMapInternalAsync(zip);
        var stopTypes    = await BuildStopRouteTypesAsync(zip, tripRouteMap, routeTypes);

        var result   = new List<StopDto>();
        var entry    = GetGtfsEntry(zip, "stops.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iId                = Col(cols, "stop_id");
        int iName              = Col(cols, "stop_name");
        int iLat               = Col(cols, "stop_lat");
        int iLon               = Col(cols, "stop_lon");
        int iLocType           = Col(cols, "location_type");
        if (iId < 0 || iName < 0 || iLat < 0 || iLon < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iId, Math.Max(iName, Math.Max(iLat, iLon)))) continue;

            // Skip parent stations
            if (iLocType >= 0 && iLocType < v.Count && Cell(v, iLocType) == "1") continue;

            var stopId = Cell(v, iId);
            var lat    = Double(Cell(v, iLat));
            var lon    = Double(Cell(v, iLon));
            if (string.IsNullOrEmpty(stopId) || (lat == 0 && lon == 0)) continue;

            result.Add(new StopDto
            {
                StopId    = stopId,
                Name      = Cell(v, iName),
                Lat       = lat,
                Lon       = lon,
                RouteType = stopTypes.TryGetValue(stopId, out var rt) ? rt : 3,
            });
        }
        return result;
    }

    public async Task<List<RouteDto>> ParseRoutesAsync(byte[] data)
    {
        using var zip = Open(data);

        var routeTypes    = await ParseRouteTypesAsync(zip);
        var shapes        = await ParseShapesAsync(zip);
        var routeShapeMap = await BuildRouteShapeMapAsync(zip);

        var result = new List<RouteDto>();
        var entry  = GetGtfsEntry(zip, "routes.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iId                = Col(cols, "route_id");
        int iShort             = Col(cols, "route_short_name");
        int iLong              = Col(cols, "route_long_name");
        int iColor             = Col(cols, "route_color");
        if (iId < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v       = Split(line);
            if (v.Count <= iId) continue;
            var routeId = Cell(v, iId);
            if (string.IsNullOrEmpty(routeId)) continue;

            var color = iColor >= 0 && iColor < v.Count ? Cell(v, iColor) : null;
            var route = new RouteDto
            {
                RouteId   = routeId,
                ShortName = iShort >= 0 && iShort < v.Count && !string.IsNullOrEmpty(Cell(v, iShort)) ? Cell(v, iShort)
                          : iLong  >= 0 && iLong  < v.Count && !string.IsNullOrEmpty(Cell(v, iLong))  ? Cell(v, iLong)
                          : routeId,
                LongName  = iLong  >= 0 && iLong  < v.Count ? Cell(v, iLong)  : "",
                Color     = string.IsNullOrWhiteSpace(color) ? null : color,
                RouteType = routeTypes.TryGetValue(routeId, out var rt) ? rt : 3,
            };

            if (routeShapeMap.TryGetValue(routeId, out var shapeId)
                && shapes.TryGetValue(shapeId, out var coords))
                route.Shape = coords;

            result.Add(route);
        }
        return result;
    }

    public async Task<Dictionary<string, string>> ParseTripRouteMapAsync(byte[] data)
    {
        using var zip = Open(data);
        return await ParseTripRouteMapInternalAsync(zip);
    }

    public async Task<Dictionary<string, (string RouteId, string Headsign)>>
        ParseTripInfoMapAsync(byte[] data, HashSet<string>? serviceIds)
    {
        using var zip = Open(data);
        return await ParseTripInfoMapInternalAsync(zip, serviceIds);
    }

    public async Task<HashSet<string>> GetActiveServiceIdsAsync(byte[] data, DateOnly date)
    {
        using var zip = Open(data);
        return await GetActiveServiceIdsInternalAsync(zip, date);
    }

    public async Task<List<DepartureGroupDto>> ParseStopScheduleAsync(
        byte[] data, string stopId, DateOnly date)
    {
        using var zip = Open(data);

        var activeIds = await GetActiveServiceIdsInternalAsync(zip, date);
        if (activeIds.Count == 0) return [];

        var tripInfo = await ParseTripInfoMapInternalAsync(zip, activeIds);
        if (tripInfo.Count == 0) return [];

        var departures = await StreamStopDeparturesAsync(zip, stopId, tripInfo);
        var routeNames = await ParseRouteNamesAsync(zip);

        // Filter past departures — 1 min grace so "leaving now" still shows
        int nowMins = DateTime.Now.Hour * 60 + DateTime.Now.Minute - 1;

        return departures
            .GroupBy(d => (d.RouteId, d.Headsign))
            .Select(g =>
            {
                var deps = g
                    .Select(d => new DepartureDto
                    {
                        TripId        = d.TripId,
                        ScheduledTime = d.Time,
                    })
                    .Where(d => Mins(d.ScheduledTime) >= nowMins)
                    .OrderBy(d => Mins(d.ScheduledTime))
                    .GroupBy(d => d.ScheduledTime)
                    .Select(tg => tg.First())   // deduplicate exact same time
                    .ToList();

                if (deps.Count == 0) return null;

                return new DepartureGroupDto
                {
                    RouteId    = g.Key.RouteId,
                    ShortName  = routeNames.TryGetValue(g.Key.RouteId, out var n)
                                 ? n : g.Key.RouteId,
                    Headsign   = g.Key.Headsign,
                    Departures = deps,
                };
            })
            .Where(g => g != null)
            .Cast<DepartureGroupDto>()
            .OrderBy(g => Mins(g.Departures.First().ScheduledTime))
            .ToList();
    }

    public async Task<List<TripStopDto>> ParseTripStopsAsync(byte[] data, string tripId)
    {
        using var zip  = Open(data);
        var stopMeta   = await ParseStopMetaAsync(zip);
        var result     = await StreamTripStopsAsync(zip, tripId, stopMeta);
        result.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        return result;
    }

    public async Task<HashSet<int>> ParseUsedRouteTypesAsync(byte[] data)
    {
        using var zip = Open(data);
        var result = new HashSet<int>();
        var entry = GetGtfsEntry(zip, "routes.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var cols = Header(await reader.ReadLineAsync());
        int iType = Col(cols, "route_type");
        if (iType < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= iType) continue;
            if (int.TryParse(Cell(v, iType), out int rt))
                result.Add(rt);
        }
        return result;
    }

    // ── Internal parsers ──────────────────────────────────────────────────

    private static async Task<Dictionary<string, int>> ParseRouteTypesAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var entry  = GetGtfsEntry(zip, "routes.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iId                = Col(cols, "route_id");
        int iType              = Col(cols, "route_type");
        if (iId < 0 || iType < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iId, iType)) continue;
            var rid = Cell(v, iId);
            if (!string.IsNullOrEmpty(rid) && int.TryParse(Cell(v, iType), out int rt))
                result.TryAdd(rid, rt);
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> ParseRouteNamesAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry  = GetGtfsEntry(zip, "routes.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iId                = Col(cols, "route_id");
        int iName              = Col(cols, "route_short_name");
        int iLong              = Col(cols, "route_long_name");
        if (iId < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= iId) continue;
            var id        = Cell(v, iId);
            var shortName = iName >= 0 && iName < v.Count ? Cell(v, iName) : "";
            var longName  = iLong >= 0 && iLong  < v.Count ? Cell(v, iLong) : "";
            var name      = !string.IsNullOrEmpty(shortName) ? shortName
                          : !string.IsNullOrEmpty(longName)  ? longName
                          : id;
            if (!string.IsNullOrEmpty(id)) result.TryAdd(id, name);
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> ParseTripRouteMapInternalAsync(
        ZipArchive zip)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry  = GetGtfsEntry(zip, "trips.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iTrip              = Col(cols, "trip_id");
        int iRoute             = Col(cols, "route_id");
        if (iTrip < 0 || iRoute < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iTrip, iRoute)) continue;
            var tid = Cell(v, iTrip);
            var rid = Cell(v, iRoute);
            if (!string.IsNullOrEmpty(tid) && !string.IsNullOrEmpty(rid))
                result.TryAdd(tid, rid);
        }
        return result;
    }

    private static async Task<Dictionary<string, (string RouteId, string Headsign)>>
        ParseTripInfoMapInternalAsync(ZipArchive zip, HashSet<string>? serviceIds)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var entry  = GetGtfsEntry(zip, "trips.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iTrip              = Col(cols, "trip_id");
        int iRoute             = Col(cols, "route_id");
        int iService           = Col(cols, "service_id");
        int iHead              = Col(cols, "trip_headsign");
        if (iTrip < 0 || iRoute < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iTrip, iRoute)) continue;
            if (serviceIds != null && iService >= 0 && iService < v.Count)
                if (!serviceIds.Contains(Cell(v, iService))) continue;
            var tid      = Cell(v, iTrip);
            var rid      = Cell(v, iRoute);
            var headsign = iHead >= 0 && iHead < v.Count ? Cell(v, iHead) : "";
            if (!string.IsNullOrEmpty(tid))
                result.TryAdd(tid, (rid, headsign));
        }
        return result;
    }

    private static async Task<HashSet<string>> GetActiveServiceIdsInternalAsync(
        ZipArchive zip, DateOnly date)
    {
        int dateInt = date.Year * 10000 + date.Month * 100 + date.Day;
        string dayCol = date.DayOfWeek switch
        {
            DayOfWeek.Monday    => "monday",
            DayOfWeek.Tuesday   => "tuesday",
            DayOfWeek.Wednesday => "wednesday",
            DayOfWeek.Thursday  => "thursday",
            DayOfWeek.Friday    => "friday",
            DayOfWeek.Saturday  => "saturday",
            _                   => "sunday"
        };

        var active = new HashSet<string>(StringComparer.Ordinal);

        // calendar.txt — regular weekly schedule
        var calEntry = GetGtfsEntry(zip, "calendar.txt");
        if (calEntry is not null)
        {
            await using var stream = calEntry.Open();
            using var reader       = new StreamReader(stream);
            var cols               = Header(await reader.ReadLineAsync());
            int iSvc               = Col(cols, "service_id");
            int iStart             = Col(cols, "start_date");
            int iEnd               = Col(cols, "end_date");
            int iDay               = Col(cols, dayCol);
            if (iSvc >= 0 && iStart >= 0 && iEnd >= 0 && iDay >= 0)
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    var v = Split(line);
                    if (v.Count <= Math.Max(iSvc, Math.Max(iStart, Math.Max(iEnd, iDay)))) continue;
                    if (!int.TryParse(Cell(v, iStart), out int s)) continue;
                    if (!int.TryParse(Cell(v, iEnd),   out int e)) continue;
                    if (dateInt >= s && dateInt <= e && Cell(v, iDay) == "1")
                        active.Add(Cell(v, iSvc));
                }
            }
        }

        // calendar_dates.txt — exceptions (added or removed days)
        var datesEntry = GetGtfsEntry(zip, "calendar_dates.txt");
        if (datesEntry is not null)
        {
            await using var stream = datesEntry.Open();
            using var reader       = new StreamReader(stream);
            var cols               = Header(await reader.ReadLineAsync());
            int iSvc               = Col(cols, "service_id");
            int iDate              = Col(cols, "date");
            int iType              = Col(cols, "exception_type");
            if (iSvc >= 0 && iDate >= 0 && iType >= 0)
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    var v = Split(line);
                    if (v.Count <= Math.Max(iSvc, Math.Max(iDate, iType))) continue;
                    if (!int.TryParse(Cell(v, iDate), out int d) || d != dateInt) continue;
                    var sid  = Cell(v, iSvc);
                    var type = Cell(v, iType);
                    if (type == "1")      active.Add(sid);
                    else if (type == "2") active.Remove(sid);
                }
            }
        }

        return active;
    }

    private static async Task<Dictionary<string, int>> BuildStopRouteTypesAsync(
        ZipArchive zip,
        Dictionary<string, string> tripRouteMap,
        Dictionary<string, int> routeTypes)
    {
        // Scan stop_times.txt to find the dominant route type per stop.
        // tram (0) beats bus (3) — a stop served by both gets the tram icon.
        var result  = new Dictionary<string, int>(StringComparer.Ordinal);
        var stEntry = GetGtfsEntry(zip, "stop_times.txt");
        if (stEntry is null) return result;

        await using var stream = stEntry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iTrip              = Col(cols, "trip_id");
        int iStop              = Col(cols, "stop_id");
        if (iTrip < 0 || iStop < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iTrip, iStop)) continue;
            var sid = Cell(v, iStop);
            var tid = Cell(v, iTrip);
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(tid)) continue;
            if (!tripRouteMap.TryGetValue(tid, out var rid)) continue;
            if (!routeTypes.TryGetValue(rid, out var rt)) continue;
            if (!result.TryGetValue(sid, out var existing) || rt < existing)
                result[sid] = rt;
        }
        return result;
    }

    private static async Task<Dictionary<string, List<double[]>>> ParseShapesAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, List<double[]>>();
        var entry  = GetGtfsEntry(zip, "shapes.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iId                = Col(cols, "shape_id");
        int iLat               = Col(cols, "shape_pt_lat");
        int iLon               = Col(cols, "shape_pt_lon");
        int iSeq               = Col(cols, "shape_pt_sequence");
        if (iId < 0 || iLat < 0 || iLon < 0) return result;

        var raw = new Dictionary<string, List<(double lon, double lat, int seq)>>();
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v       = Split(line);
            if (v.Count <= Math.Max(iId, Math.Max(iLat, iLon))) continue;
            var shapeId = Cell(v, iId);
            var lat     = Double(Cell(v, iLat));
            var lon     = Double(Cell(v, iLon));
            var seq     = iSeq >= 0 && iSeq < v.Count
                          && int.TryParse(Cell(v, iSeq), out int s) ? s : 0;
            if (!raw.ContainsKey(shapeId)) raw[shapeId] = [];
            raw[shapeId].Add((lon, lat, seq));
        }

        foreach (var (id, pts) in raw)
            result[id] = pts
                .OrderBy(p => p.seq)
                .Select(p => new[] { p.lon, p.lat })
                .ToList();

        return result;
    }

    private static async Task<Dictionary<string, string>> BuildRouteShapeMapAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry  = GetGtfsEntry(zip, "trips.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iRoute             = Col(cols, "route_id");
        int iShape             = Col(cols, "shape_id");
        if (iRoute < 0 || iShape < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iRoute, iShape)) continue;
            var rid = Cell(v, iRoute);
            var sid = Cell(v, iShape);
            if (!string.IsNullOrEmpty(rid) && !string.IsNullOrEmpty(sid))
                result.TryAdd(rid, sid);
        }
        return result;
    }

    private sealed record RawDeparture(
        string RouteId, string Headsign, string TripId, string Time);

    private static async Task<List<RawDeparture>> StreamStopDeparturesAsync(
        ZipArchive zip,
        string stopId,
        Dictionary<string, (string RouteId, string Headsign)> tripInfo)
    {
        var result = new List<RawDeparture>();
        var entry  = GetGtfsEntry(zip, "stop_times.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iTripId            = Col(cols, "trip_id");
        int iStopId            = Col(cols, "stop_id");
        int iDep               = Col(cols, "departure_time");
        if (iDep < 0) iDep     = Col(cols, "arrival_time");
        if (iTripId < 0 || iStopId < 0 || iDep < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iTripId, Math.Max(iStopId, iDep))) continue;
            if (!string.Equals(Cell(v, iStopId), stopId, StringComparison.OrdinalIgnoreCase))
                continue;
            var tripId = Cell(v, iTripId);
            if (!tripInfo.TryGetValue(tripId, out var info)) continue;
            var dep    = Trim5(Cell(v, iDep));
            if (!string.IsNullOrEmpty(dep))
                result.Add(new RawDeparture(info.RouteId, info.Headsign, tripId, dep));
        }
        return result;
    }

    private static async Task<Dictionary<string, (string Name, double Lat, double Lon)>>
        ParseStopMetaAsync(ZipArchive zip)
    {
        var result = new Dictionary<string, (string, double, double)>(StringComparer.Ordinal);
        var entry  = GetGtfsEntry(zip, "stops.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iId                = Col(cols, "stop_id");
        int iName              = Col(cols, "stop_name");
        int iLat               = Col(cols, "stop_lat");
        int iLon               = Col(cols, "stop_lon");
        if (iId < 0 || iName < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v    = Split(line);
            if (v.Count <= iId) continue;
            var sid  = Cell(v, iId);
            var name = iName < v.Count ? Cell(v, iName) : sid;
            var lat  = iLat >= 0 && iLat < v.Count ? Double(Cell(v, iLat)) : 0;
            var lon  = iLon >= 0 && iLon < v.Count ? Double(Cell(v, iLon)) : 0;
            if (!string.IsNullOrEmpty(sid)) result.TryAdd(sid, (name, lat, lon));
        }
        return result;
    }

    private static async Task<List<TripStopDto>> StreamTripStopsAsync(
        ZipArchive zip,
        string tripId,
        Dictionary<string, (string Name, double Lat, double Lon)> stopMeta)
    {
        var result = new List<TripStopDto>();
        var entry  = GetGtfsEntry(zip, "stop_times.txt");
        if (entry is null) return result;

        await using var stream = entry.Open();
        using var reader       = new StreamReader(stream);
        var cols               = Header(await reader.ReadLineAsync());
        int iTripId            = Col(cols, "trip_id");
        int iStopId            = Col(cols, "stop_id");
        int iSeq               = Col(cols, "stop_sequence");
        int iDep               = Col(cols, "departure_time");
        if (iDep < 0) iDep     = Col(cols, "arrival_time");
        if (iTripId < 0 || iStopId < 0 || iSeq < 0 || iDep < 0) return result;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var v = Split(line);
            if (v.Count <= Math.Max(iTripId, Math.Max(iStopId, Math.Max(iSeq, iDep)))) continue;
            if (!string.Equals(Cell(v, iTripId), tripId, StringComparison.OrdinalIgnoreCase))
                continue;

            var sid = Cell(v, iStopId);
            var dep = Trim5(Cell(v, iDep));
            var seq = int.TryParse(Cell(v, iSeq), out int s) ? s : 0;
            stopMeta.TryGetValue(sid, out var meta);

            result.Add(new TripStopDto
            {
                Sequence      = seq,
                StopId        = sid,
                StopName      = meta.Name ?? sid,
                Lat           = meta.Lat,
                Lon           = meta.Lon,
                ScheduledTime = dep,
            });
        }
        return result;
    }


    // ── CSV utilities ─────────────────────────────────────────────────────

    private static ZipArchive Open(byte[] data)
        => new(new MemoryStream(data), ZipArchiveMode.Read);

    private static ZipArchiveEntry? GetGtfsEntry(ZipArchive zip, string fileName)
    {
        ZipArchiveEntry? bestNestedMatch = null;

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory
            if (!string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(entry.FullName, fileName, StringComparison.OrdinalIgnoreCase))
                return entry; // exact root-level match wins

            if (bestNestedMatch is null
                || string.Compare(entry.FullName, bestNestedMatch.FullName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                bestNestedMatch = entry;
            }
        }

        return bestNestedMatch;
    }

    private static List<string> Header(string? line)
        => Split(line?.TrimStart('\uFEFF') ?? "");

    private static List<string> Split(string line)
    {
        var result    = new List<string>();
        var current   = new System.Text.StringBuilder();
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

    private static string Cell(List<string> row, int idx)
        => idx >= 0 && idx < row.Count ? row[idx].Trim().Trim('"').Trim() : "";

    private static int Col(List<string> cols, string name)
    {
        for (int i = 0; i < cols.Count; i++)
            if (string.Equals(cols[i].Trim().Trim('"').Trim(), name,
                StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static double Double(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    /// <summary>Trims time strings to HH:MM — handles "25:30:00" after-midnight times.</summary>
    private static string Trim5(string t)
        => t.Length > 5 && t[2] == ':' ? t[..5] : t;

    private static int Mins(string t)
    {
        var p = t.Split(':');
        return p.Length >= 2
               && int.TryParse(p[0], out int h)
               && int.TryParse(p[1], out int m)
            ? h * 60 + m : 0;
    }
}
