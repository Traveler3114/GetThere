using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

using CsvHelper;
using CsvHelper.Configuration;

using Microsoft.Extensions.Logging;

using NetTopologySuite.Geometries;
using NetTopologySuite.Algorithm;

using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services;

public class GtfsParser
{
    private readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);
    private readonly ILogger<GtfsParser> _logger;

    public GtfsParser(ILogger<GtfsParser> logger)
    {
        _logger = logger;
    }

    public string ComputeGtfsSha1(ZipArchive archive)
    {
        var entries = archive.Entries
            .Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var sha1 = SHA1.Create();
        foreach (var entry in entries)
        {
            using var stream = entry.Open();
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                sha1.TransformBlock(buffer, 0, read, null, 0);
        }
        sha1.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha1.Hash!).ToLowerInvariant();
    }

    public GtfsValidationResult ValidateGtfs(ZipArchive archive)
    {
        var result = new GtfsValidationResult();
        try
        {
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
            if (!fileNames.Contains("calendar.txt") && !fileNames.Contains("calendar_dates.txt"))
                result.Errors.Add("Missing calendar data (calendar.txt or calendar_dates.txt required)");

            result.HasCalendar = fileNames.Contains("calendar.txt");
            result.HasCalendarDates = fileNames.Contains("calendar_dates.txt");
            result.HasShapes = fileNames.Contains("shapes.txt");

            result.IsValid = result.HasAgency && result.HasStops && result.HasRoutes && result.HasTrips && result.HasStopTimes
                && (result.HasCalendar || result.HasCalendarDates);
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Failed to read zip: {ex.Message}");
        }
        return result;
    }

    public List<RawAgencyRecord> ParseAgencies(ZipArchive archive)
    {
        return ParseCsv<RawAgencyRecord>(archive, "agency.txt", cfg =>
        {
            cfg.RegisterClassMap(new AgencyMap());
        });
    }

    public (List<RawStopRecord> Stops, int DroppedCount) ParseStops(ZipArchive archive)
    {
        var stops = ParseCsv<RawStopRecord>(archive, "stops.txt", cfg =>
        {
            cfg.RegisterClassMap(new StopMap());
        });

        var valid = new List<RawStopRecord>(stops.Count);
        var dropped = 0;
        foreach (var stop in stops)
        {
            if (stop.StopLat < -90 || stop.StopLat > 90 || stop.StopLon < -180 || stop.StopLon > 180
                || (stop.StopLat == 0.0 && stop.StopLon == 0.0))
            {
                _logger.LogWarning("Skipping stop {StopId} with invalid coordinates ({Lat}, {Lon})",
                    stop.StopId, stop.StopLat, stop.StopLon);
                dropped++;
            }
            else
            {
                valid.Add(stop);
            }
        }

        return (valid, dropped);
    }

    public List<RawRouteRecord> ParseRoutes(ZipArchive archive)
    {
        return ParseCsv<RawRouteRecord>(archive, "routes.txt", cfg =>
        {
            cfg.RegisterClassMap(new RouteMap());
        });
    }

    public List<RawTripRecord> ParseTrips(ZipArchive archive)
    {
        return ParseCsv<RawTripRecord>(archive, "trips.txt", cfg =>
        {
            cfg.RegisterClassMap(new TripMap());
        });
    }

    public IAsyncEnumerable<List<RawStopTimeRecord>> ParseStopTimesBatchedAsync(ZipArchive archive, int batchSize = 1000)
    {
        return ParseCsvBatchedAsync<RawStopTimeRecord>(archive, "stop_times.txt", batchSize, cfg =>
        {
            cfg.RegisterClassMap(new StopTimeMap());
        });
    }

    public Dictionary<string, LineString> ParseShapes(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals("shapes.txt", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return [];

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig);
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

    public List<RawCalendarRecord> ParseCalendar(ZipArchive archive)
    {
        return ParseCsv<RawCalendarRecord>(archive, "calendar.txt", cfg =>
        {
            cfg.RegisterClassMap(new CalendarMap());
        });
    }

    public List<RawCalendarDateRecord> ParseCalendarDates(ZipArchive archive)
    {
        return ParseCsv<RawCalendarDateRecord>(archive, "calendar_dates.txt", cfg =>
        {
            cfg.RegisterClassMap(new CalendarDateMap());
        });
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

    private List<T> ParseCsv<T>(ZipArchive archive, string fileName, Action<CsvContext>? configure = null)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return [];

        using var csvReader = new StreamReader(entry.Open());
        using var csv = new CsvReader(csvReader, CsvConfig);
        configure?.Invoke(csv.Context);
        return csv.GetRecords<T>().ToList();
    }

    private async IAsyncEnumerable<List<T>> ParseCsvBatchedAsync<T>(
        ZipArchive archive, string fileName, int batchSize, Action<CsvContext>? configure = null)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) yield break;

        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CsvConfig);
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

    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null,
        ReadingExceptionOccurred = null,
        TrimOptions = TrimOptions.Trim,
        AllowComments = false
    };
    public static int? ParseGtfsTimeToSeconds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Length == 7) raw = "0" + raw;
        var parts = raw.Split(':');
        if (parts.Length == 3
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m)
            && int.TryParse(parts[2], out var s))
            return h * 3600 + m * 60 + s;
        return null;
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
    public int? LocationType { get; set; }
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
        1 => RouteType.Subway,
        2 => RouteType.Train,
        3 => RouteType.Bus,
        4 => RouteType.Ferry,
        5 => RouteType.CableTram,
        6 => RouteType.CableCar,
        7 => RouteType.Funicular,
        11 => RouteType.Trolleybus,
        12 => RouteType.Monorail,
        100 => RouteType.Train,
        101 => RouteType.Train,
        102 => RouteType.Train,
        103 => RouteType.Train,
        104 => RouteType.Train,
        105 => RouteType.Train,
        106 => RouteType.Train,
        107 => RouteType.Train,
        108 => RouteType.Train,
        109 => RouteType.Train,
        200 => RouteType.Bus,
        201 => RouteType.Bus,
        202 => RouteType.Bus,
        204 => RouteType.Bus,
        400 => RouteType.Subway,
        401 => RouteType.Subway,
        402 => RouteType.Subway,
        403 => RouteType.Subway,
        404 => RouteType.Subway,
        405 => RouteType.Subway,
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
         1100 => RouteType.Airplane,
         1200 => RouteType.Ferry,
        1300 => RouteType.CableCar,
        1400 => RouteType.Funicular,
        1500 => RouteType.Airplane,
         1700 => RouteType.Bus,
         203 => RouteType.Bus,
        _ => RouteType.Bus
    };
}
