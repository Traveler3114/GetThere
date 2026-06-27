using System.IO.Compression;
using System.Text;

using Microsoft.Extensions.Logging;

namespace TransitInfoAPI.Writers;

public class GtfsStaticWriter
{
    private readonly ILogger<GtfsStaticWriter> _logger;
    private readonly IWebHostEnvironment _env;

    public GtfsStaticWriter(ILogger<GtfsStaticWriter> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async Task<int> WriteAsync(
        List<Dictionary<string, object?>> records,
        string? targetTable,
        int operatorId,
        string? feedId,
        CancellationToken ct)
    {
        if (records.Count == 0) return 0;
        if (string.IsNullOrWhiteSpace(feedId))
        {
            _logger.LogWarning("GtfsStaticWriter: feedId is null or empty, cannot write");
            return 0;
        }

        var table = ResolveTable(records, targetTable);
        if (table is null)
        {
            _logger.LogWarning("GtfsStaticWriter: could not resolve target table from records");
            return 0;
        }

        var outputDir = Path.Combine(_env.ContentRootPath, "feeds", feedId);
        Directory.CreateDirectory(outputDir);
        var zipPath = Path.Combine(outputDir, "gtfs.zip");
        var tmpPath = zipPath + ".tmp";

        var csvContent = BuildCsv(records, table.Columns);

        using var tmpZip = new FileStream(tmpPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(tmpZip, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(table.FileName, CompressionLevel.Optimal);
        await using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            await writer.WriteAsync(csvContent);
        }

        try
        {
            File.Replace(tmpPath, zipPath, null);
        }
        catch (FileNotFoundException)
        {
            File.Move(tmpPath, zipPath);
        }

        _logger.LogInformation(
            "GtfsStaticWriter: wrote {Count} records to {File} for feed {FeedId}",
            records.Count, table.FileName, feedId);

        return records.Count;
    }

    private static TableInfo? ResolveTable(List<Dictionary<string, object?>> records, string? targetTable)
    {
        if (targetTable is not null)
        {
            var known = KnownTables.FirstOrDefault(t =>
                t.Name.Equals(targetTable, StringComparison.OrdinalIgnoreCase) ||
                t.FileName.Equals(targetTable, StringComparison.OrdinalIgnoreCase));
            return known;
        }

        if (records.Count == 0) return null;

        var keys = records[0].Keys;

        if (keys.Contains("stop_id"))
            return KnownTables.First(t => t.Name == "stops");
        if (keys.Contains("route_id") && !keys.Contains("trip_id"))
            return KnownTables.First(t => t.Name == "routes");
        if (keys.Contains("trip_id") && keys.Contains("stop_sequence"))
            return KnownTables.First(t => t.Name == "stop_times");
        if (keys.Contains("trip_id") && !keys.Contains("stop_sequence"))
            return KnownTables.First(t => t.Name == "trips");
        if (keys.Contains("service_id") && keys.Contains("start_date"))
            return KnownTables.First(t => t.Name == "calendar");
        if (keys.Contains("shape_id") && keys.Contains("shape_pt_sequence"))
            return KnownTables.First(t => t.Name == "shapes");
        if (keys.Contains("agency_name"))
            return KnownTables.First(t => t.Name == "agency");

        return null;
    }

    private static string BuildCsv(List<Dictionary<string, object?>> records, string[] columns)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < columns.Length; i++)
        {
            sb.Append(columns[i]);
            if (i < columns.Length - 1) sb.Append(',');
        }
        sb.AppendLine();

        foreach (var record in records)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                var val = record.TryGetValue(columns[i], out var v) ? v?.ToString() : null;
                AppendCsvValue(sb, val ?? string.Empty);
                if (i < columns.Length - 1) sb.Append(',');
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendCsvValue(StringBuilder sb, string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            sb.Append('"');
            sb.Append(value.Replace("\"", "\"\""));
            sb.Append('"');
        }
        else
        {
            sb.Append(value);
        }
    }

    private record TableInfo(string Name, string FileName, string[] Columns);

    private static readonly TableInfo[] KnownTables =
    [
        new("stops", "stops.txt", ["stop_id", "stop_code", "stop_name", "stop_desc", "stop_lat", "stop_lon", "zone_id", "stop_url", "location_type", "parent_station", "wheelchair_boarding"]),
        new("routes", "routes.txt", ["route_id", "agency_id", "route_short_name", "route_long_name", "route_desc", "route_type", "route_url", "route_color", "route_text_color"]),
        new("trips", "trips.txt", ["route_id", "service_id", "trip_id", "trip_headsign", "direction_id", "block_id", "shape_id", "wheelchair_accessible", "bikes_allowed"]),
        new("stop_times", "stop_times.txt", ["trip_id", "arrival_time", "departure_time", "stop_id", "stop_sequence", "stop_headsign", "pickup_type", "drop_off_type", "shape_dist_traveled"]),
        new("calendar", "calendar.txt", ["service_id", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "start_date", "end_date"]),
        new("calendar_dates", "calendar_dates.txt", ["service_id", "date", "exception_type"]),
        new("shapes", "shapes.txt", ["shape_id", "shape_pt_lat", "shape_pt_lon", "shape_pt_sequence", "shape_dist_traveled"]),
        new("agency", "agency.txt", ["agency_id", "agency_name", "agency_url", "agency_timezone", "agency_lang", "agency_phone", "agency_fare_url"])
    ];
}
