using System.IO.Compression;
using System.Text;

using Microsoft.Extensions.Logging;

namespace TransitInfoAPI.Writers;

public class GtfsStaticWriter
{
    private readonly ILogger<GtfsStaticWriter> _logger;

    public GtfsStaticWriter(ILogger<GtfsStaticWriter> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> ConvertAsync(
        List<Dictionary<string, object?>> records,
        string? targetTable,
        CancellationToken ct)
    {
        if (records.Count == 0) return [];

        var table = ResolveTable(records, targetTable);
        if (table is null)
        {
            _logger.LogWarning("GtfsStaticWriter: could not resolve target table from records");
            return [];
        }

        var tableRecords = new Dictionary<string, List<Dictionary<string, object?>>>
        {
            [table.Name] = records
        };

        return await ConvertMultiAsync(tableRecords, ct);
    }

    public async Task<byte[]> ConvertAsync(
        Dictionary<string, List<Dictionary<string, object?>>> tableRecords,
        CancellationToken ct)
    {
        return await ConvertMultiAsync(tableRecords, ct);
    }

    private async Task<byte[]> ConvertMultiAsync(
        Dictionary<string, List<Dictionary<string, object?>>> tableRecords,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (tableName, records) in tableRecords)
            {
                if (records.Count == 0) continue;

                // Find table by name or detect from columns
                var table = KnownTables.FirstOrDefault(t =>
                    t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase) ||
                    t.FileName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

                if (table is null)
                {
                    // Auto-detect from first record's keys
                    table = DetectTable(records[0]);
                }

                if (table is null)
                {
                    _logger.LogWarning(
                        "GtfsStaticWriter: could not resolve table '{Table}' from records, skipping",
                        tableName);
                    continue;
                }

                var csvContent = BuildCsv(records, table.Columns);
                var entry = archive.CreateEntry(table.FileName, CompressionLevel.Optimal);
                await using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    await writer.WriteAsync(csvContent);
                }

                _logger.LogInformation(
                    "GtfsStaticWriter: converted {Count} records to {File}",
                    records.Count, table.FileName);
            }
        }

        return ms.ToArray();
    }

    private static TableInfo? DetectTable(Dictionary<string, object?> firstRecord)
    {
        var keys = firstRecord.Keys;
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
        return KnownTables.FirstOrDefault(t => t.Name == "calendar_dates");
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
