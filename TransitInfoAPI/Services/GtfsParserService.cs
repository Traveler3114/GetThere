using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

using CsvHelper;
using CsvHelper.Configuration;

using NetTopologySuite.Geometries;
using NetTopologySuite.Algorithm;

using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services;

public class GtfsParserService
{
    private readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);

    public string ComputeGtfsSha1(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries
            .Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var sha1 = SHA1.Create();
        foreach (var entry in entries)
        {
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            sha1.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha1.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha1.Hash!).ToLowerInvariant();
    }

    public GtfsValidationResult ValidateGtfs(string zipPath)
    {
        var result = new GtfsValidationResult();
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var fileNames = archive.Entries
                .Select(e => Path.GetFileName(e.FullName))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            result.HasAgency = fileNames.Contains("agency.txt");
            result.HasStops = fileNames.Contains("stops.txt");
            result.HasRoutes = fileNames.Contains("routes.txt");
            result.HasTrips = fileNames.Contains("trips.txt");
            result.HasStopTimes = fileNames.Contains("stop_times.txt");

            if (!result.HasAgency) result.Errors.Add("Missing agency.txt");
            if (!result.HasStops) result.Errors.Add("Missing stops.txt");
            if (!result.HasRoutes) result.Errors.Add("Missing routes.txt");
            if (!result.HasTrips) result.Errors.Add("Missing trips.txt");
            if (!result.HasStopTimes) result.Errors.Add("Missing stop_times.txt");

            result.HasCalendar = fileNames.Contains("calendar.txt");
            result.HasCalendarDates = fileNames.Contains("calendar_dates.txt");
            result.HasShapes = fileNames.Contains("shapes.txt");

            result.IsValid = result.HasAgency && result.HasStops && result.HasRoutes && result.HasTrips && result.HasStopTimes;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Failed to read zip: {ex.Message}");
        }
        return result;
    }

    public List<RawAgencyRecord> ParseAgencies(string zipPath)
    {
        return ParseCsv<RawAgencyRecord>(zipPath, "agency.txt", cfg =>
        {
            cfg.RegisterClassMap(new AgencyMap());
        });
    }

    public List<RawStopRecord> ParseStops(string zipPath)
    {
        return ParseCsv<RawStopRecord>(zipPath, "stops.txt", cfg =>
        {
            cfg.RegisterClassMap(new StopMap());
        });
    }

    public List<RawRouteRecord> ParseRoutes(string zipPath)
    {
        return ParseCsv<RawRouteRecord>(zipPath, "routes.txt", cfg =>
        {
            cfg.RegisterClassMap(new RouteMap());
        });
    }

    public List<RawTripRecord> ParseTrips(string zipPath)
    {
        return ParseCsv<RawTripRecord>(zipPath, "trips.txt", cfg =>
        {
            cfg.RegisterClassMap(new TripMap());
        });
    }

    public IAsyncEnumerable<List<RawStopTimeRecord>> ParseStopTimesBatchedAsync(string zipPath, int batchSize = 1000)
    {
        return ParseCsvBatchedAsync<RawStopTimeRecord>(zipPath, "stop_times.txt", batchSize, cfg =>
        {
            cfg.RegisterClassMap(new StopTimeMap());
        });
    }

    public Dictionary<string, LineString> ParseShapes(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals("shapes.txt", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return [];

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());
        csv.Context.RegisterClassMap(new ShapePointMap());

        var pointsByShape = new Dictionary<string, List<ShapePointRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in csv.GetRecords<ShapePointRecord>())
        {
            if (!pointsByShape.ContainsKey(record.ShapeId))
                pointsByShape[record.ShapeId] = [];
            pointsByShape[record.ShapeId].Add(record);
        }

        var result = new Dictionary<string, LineString>();
        foreach (var kvp in pointsByShape)
        {
            var ordered = kvp.Value.OrderBy(s => s.ShapePtSequence).ToList();
            var coords = ordered.Select(s => new Coordinate(s.ShapePtLon, s.ShapePtLat)).ToArray();
            result[kvp.Key] = _geometryFactory.CreateLineString(coords);
        }
        return result;
    }

    public List<RawCalendarRecord> ParseCalendar(string zipPath)
    {
        return ParseCsv<RawCalendarRecord>(zipPath, "calendar.txt", cfg =>
        {
            cfg.RegisterClassMap(new CalendarMap());
        });
    }

    public List<RawCalendarDateRecord> ParseCalendarDates(string zipPath)
    {
        return ParseCsv<RawCalendarDateRecord>(zipPath, "calendar_dates.txt", cfg =>
        {
            cfg.RegisterClassMap(new CalendarDateMap());
        });
    }

    public Dictionary<string, RouteType> DeriveRouteTypesPerStop(
        List<RawRouteRecord> routes,
        List<RawTripRecord> trips,
        List<RawStopTimeRecord> stopTimes)
    {
        var routeTypesByRoute = routes
            .GroupBy(r => r.RouteId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RouteTypeEnum, StringComparer.OrdinalIgnoreCase);

        var routeByTrip = trips
            .GroupBy(t => t.TripId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RouteId, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, RouteType>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in stopTimes)
        {
            if (!routeByTrip.TryGetValue(st.TripId, out var routeId)) continue;
            if (!routeTypesByRoute.TryGetValue(routeId, out var rt)) continue;

            if (!result.ContainsKey(st.StopId))
                result[st.StopId] = rt;
        }
        return result;
    }

    public Geometry ComputeConvexHull(List<RawStopRecord> stops)
    {
        if (stops.Count == 0) return _geometryFactory.CreatePoint();
        var coords = stops.Select(s => new Coordinate(s.StopLon, s.StopLat)).ToArray();
        var hull = new ConvexHull(coords, _geometryFactory).GetConvexHull();

        if (hull is Polygon polygon && !Orientation.IsCCW(polygon.Shell.Coordinates))
            hull = polygon.Reverse();

        return hull;
    }

    private List<T> ParseCsv<T>(string zipPath, string fileName, Action<CsvContext>? configure = null)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return [];

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());
        configure?.Invoke(csv.Context);
        return csv.GetRecords<T>().ToList();
    }

    private async IAsyncEnumerable<List<T>> ParseCsvBatchedAsync<T>(
        string zipPath, string fileName, int batchSize, Action<CsvContext>? configure = null)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) yield break;

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig());
        configure?.Invoke(csv.Context);

        var batch = new List<T>(batchSize);
        await foreach (var record in csv.GetRecordsAsync<T>())
        {
            batch.Add(record);
            if (batch.Count >= batchSize)
            {
                yield return batch;
                batch = new List<T>(batchSize);
            }
        }
        if (batch.Count > 0)
            yield return batch;
    }

    private static CsvConfiguration CsvConfig() => new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null,
        TrimOptions = TrimOptions.Trim,
        AllowComments = true
    };
    public static int ParseGtfsTimeToSeconds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        if (raw.Length == 7) raw = "0" + raw;
        var parts = raw.Split(':');
        if (parts.Length == 3
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m)
            && int.TryParse(parts[2], out var s))
            return h * 3600 + m * 60 + s;
        return 0;
    }
}

public class GtfsValidationResult
{
    public bool IsValid { get; set; }
    public bool HasAgency { get; set; }
    public bool HasStops { get; set; }
    public bool HasRoutes { get; set; }
    public bool HasTrips { get; set; }
    public bool HasStopTimes { get; set; }
    public bool HasCalendar { get; set; }
    public bool HasCalendarDates { get; set; }
    public bool HasShapes { get; set; }
    public List<string> Errors { get; set; } = [];
}

public record RawAgencyRecord
{
    public string AgencyId { get; set; } = string.Empty;
    public string AgencyName { get; set; } = string.Empty;
    public string? AgencyUrl { get; set; }
    public string? AgencyTimezone { get; set; }
    public string? AgencyLang { get; set; }
    public string? AgencyPhone { get; set; }
    public string? AgencyFareUrl { get; set; }
    public string? AgencyEmail { get; set; }
}

public record RawStopRecord
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public double StopLat { get; set; }
    public double StopLon { get; set; }
    public string? StopCode { get; set; }
    public string? StopDesc { get; set; }
    public string? ZoneId { get; set; }
    public string? PlatformCode { get; set; }
    public int? WheelchairBoarding { get; set; }
    public int LocationType { get; set; }
    public string? ParentStation { get; set; }
}

public record RawRouteRecord
{
    public string RouteId { get; set; } = string.Empty;
    public string RouteShortName { get; set; } = string.Empty;
    public string RouteLongName { get; set; } = string.Empty;
    public int RouteType { get; set; }
    public string? RouteColor { get; set; }
    public string? RouteTextColor { get; set; }
    public string? AgencyId { get; set; }
    public RouteType RouteTypeEnum => GtfsRouteTypeMapper.MapGtfsRouteType(RouteType);
}

public record RawTripRecord
{
    public string TripId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string? TripHeadsign { get; set; }
    public string? TripShortName { get; set; }
    public int? DirectionId { get; set; }
    public string? ShapeId { get; set; }
    public int? WheelchairAccessible { get; set; }
    public int? BikesAllowed { get; set; }
}

public record RawStopTimeRecord
{
    public string TripId { get; set; } = string.Empty;
    public string StopId { get; set; } = string.Empty;
    public string ArrivalTime { get; set; } = string.Empty;
    public string DepartureTime { get; set; } = string.Empty;
    public int StopSequence { get; set; }
    public string? StopHeadsign { get; set; }
    public int? PickupType { get; set; }
    public int? DropOffType { get; set; }
    public int? Timepoint { get; set; }
}

public record RawCalendarRecord
{
    public string ServiceId { get; set; } = string.Empty;
    public int Monday { get; set; }
    public int Tuesday { get; set; }
    public int Wednesday { get; set; }
    public int Thursday { get; set; }
    public int Friday { get; set; }
    public int Saturday { get; set; }
    public int Sunday { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
}

public record RawCalendarDateRecord
{
    public string ServiceId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int ExceptionType { get; set; }
}

internal record ShapePointRecord
{
    public string ShapeId { get; set; } = string.Empty;
    public double ShapePtLat { get; set; }
    public double ShapePtLon { get; set; }
    public int ShapePtSequence { get; set; }
}

internal class AgencyMap : ClassMap<RawAgencyRecord>
{
    public AgencyMap()
    {
        Map(m => m.AgencyId).Name("agency_id");
        Map(m => m.AgencyName).Name("agency_name");
        Map(m => m.AgencyUrl).Name("agency_url");
        Map(m => m.AgencyTimezone).Name("agency_timezone");
        Map(m => m.AgencyLang).Name("agency_lang");
        Map(m => m.AgencyPhone).Name("agency_phone");
        Map(m => m.AgencyFareUrl).Name("agency_fare_url");
        Map(m => m.AgencyEmail).Name("agency_email");
    }
}

internal class StopMap : ClassMap<RawStopRecord>
{
    public StopMap()
    {
        Map(m => m.StopId).Name("stop_id");
        Map(m => m.StopName).Name("stop_name");
        Map(m => m.StopLat).Name("stop_lat");
        Map(m => m.StopLon).Name("stop_lon");
        Map(m => m.StopCode).Name("stop_code");
        Map(m => m.StopDesc).Name("stop_desc");
        Map(m => m.ZoneId).Name("zone_id");
        Map(m => m.PlatformCode).Name("platform_code");
        Map(m => m.WheelchairBoarding).Name("wheelchair_boarding");
        Map(m => m.LocationType).Name("location_type");
        Map(m => m.ParentStation).Name("parent_station");
    }
}

internal class RouteMap : ClassMap<RawRouteRecord>
{
    public RouteMap()
    {
        Map(m => m.RouteId).Name("route_id");
        Map(m => m.RouteShortName).Name("route_short_name");
        Map(m => m.RouteLongName).Name("route_long_name");
        Map(m => m.RouteType).Name("route_type");
        Map(m => m.RouteColor).Name("route_color");
        Map(m => m.RouteTextColor).Name("route_text_color");
        Map(m => m.AgencyId).Name("agency_id");
    }
}

internal class TripMap : ClassMap<RawTripRecord>
{
    public TripMap()
    {
        Map(m => m.TripId).Name("trip_id");
        Map(m => m.RouteId).Name("route_id");
        Map(m => m.ServiceId).Name("service_id");
        Map(m => m.TripHeadsign).Name("trip_headsign");
        Map(m => m.TripShortName).Name("trip_short_name");
        Map(m => m.DirectionId).Name("direction_id");
        Map(m => m.ShapeId).Name("shape_id");
        Map(m => m.WheelchairAccessible).Name("wheelchair_accessible");
        Map(m => m.BikesAllowed).Name("bikes_allowed");
    }
}

internal class StopTimeMap : ClassMap<RawStopTimeRecord>
{
    public StopTimeMap()
    {
        Map(m => m.TripId).Name("trip_id");
        Map(m => m.StopId).Name("stop_id");
        Map(m => m.ArrivalTime).Name("arrival_time");
        Map(m => m.DepartureTime).Name("departure_time");
        Map(m => m.StopSequence).Name("stop_sequence");
        Map(m => m.StopHeadsign).Name("stop_headsign");
        Map(m => m.PickupType).Name("pickup_type");
        Map(m => m.DropOffType).Name("drop_off_type");
        Map(m => m.Timepoint).Name("timepoint");
    }
}

internal class ShapePointMap : ClassMap<ShapePointRecord>
{
    public ShapePointMap()
    {
        Map(m => m.ShapeId).Name("shape_id");
        Map(m => m.ShapePtLat).Name("shape_pt_lat");
        Map(m => m.ShapePtLon).Name("shape_pt_lon");
        Map(m => m.ShapePtSequence).Name("shape_pt_sequence");
    }
}

internal class CalendarMap : ClassMap<RawCalendarRecord>
{
    public CalendarMap()
    {
        Map(m => m.ServiceId).Name("service_id");
        Map(m => m.Monday).Name("monday");
        Map(m => m.Tuesday).Name("tuesday");
        Map(m => m.Wednesday).Name("wednesday");
        Map(m => m.Thursday).Name("thursday");
        Map(m => m.Friday).Name("friday");
        Map(m => m.Saturday).Name("saturday");
        Map(m => m.Sunday).Name("sunday");
        Map(m => m.StartDate).Name("start_date");
        Map(m => m.EndDate).Name("end_date");
    }
}

internal class CalendarDateMap : ClassMap<RawCalendarDateRecord>
{
    public CalendarDateMap()
    {
        Map(m => m.ServiceId).Name("service_id");
        Map(m => m.Date).Name("date");
        Map(m => m.ExceptionType).Name("exception_type");
    }
}

    public static class GtfsRouteTypeMapper
{
    public static RouteType MapGtfsRouteType(int gtfsType) => gtfsType switch
    {
        0 => RouteType.Tram,
        1 => RouteType.Metro,
        2 => RouteType.Rail,
        3 => RouteType.Bus,
        4 => RouteType.Ferry,
        5 => RouteType.CableCar,
        6 => RouteType.Funicular,
        7 => RouteType.Funicular,
        11 => RouteType.Trolleybus,
        100 => RouteType.Rail,
        101 => RouteType.Rail,
        102 => RouteType.Rail,
        103 => RouteType.Rail,
        104 => RouteType.Rail,
        105 => RouteType.Rail,
        106 => RouteType.Rail,
        107 => RouteType.Rail,
        108 => RouteType.Rail,
        109 => RouteType.Rail,
        200 => RouteType.Coach,
        201 => RouteType.Coach,
        202 => RouteType.Coach,
        204 => RouteType.Coach,
        400 => RouteType.Metro,
        401 => RouteType.Metro,
        402 => RouteType.Metro,
        403 => RouteType.Metro,
        404 => RouteType.Metro,
        405 => RouteType.Metro,
        700 => RouteType.Bus,
        701 => RouteType.Bus,
        702 => RouteType.Bus,
        703 => RouteType.Bus,
        704 => RouteType.Bus,
        705 => RouteType.Bus,
        715 => RouteType.Bus,
        800 => RouteType.Trolleybus,
        900 => RouteType.Tram,
        1000 => RouteType.Ferry,
        1100 => RouteType.Funicular,
        1200 => RouteType.Funicular,
        1300 => RouteType.CableCar,
        1400 => RouteType.Funicular,
        1500 => RouteType.Flight,
        1700 => RouteType.Coach,
        _ => RouteType.Bus
    };
}
