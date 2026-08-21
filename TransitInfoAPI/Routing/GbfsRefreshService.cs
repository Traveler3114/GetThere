using Microsoft.Extensions.Options;

using TransitInfoAPI.Routing.Export;

namespace TransitInfoAPI.Routing;

/// <summary>
/// Refreshes only the GBFS payloads (cache + three JSON files) without rebuilding the transit
/// <c>gtfs.zip</c>. Called after each mobility poll so OTP picks up Nextbike stations within a
/// poll cycle, and reused by the full rebuild worker so there is one GBFS code path.
/// </summary>
public sealed class GbfsRefreshService(
    GbfsExporter gbfsExporter,
    RoutingExportCache cache,
    IOptions<RoutingOptions> options,
    ILogger<GbfsRefreshService> logger)
{
    /// <summary>
    /// Exports GBFS from the current <c>MobilityStations</c> and refreshes the cache plus the three
    /// JSON files atomically. Does not touch <c>gtfs.zip</c>.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var gbfs = await gbfsExporter.ExportAsync(ct);
        await ApplyAsync(gbfs, DateTime.UtcNow, ct);
        var outputDir = options.Value.Export.OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDir))
            logger.LogInformation("GBFS refresh: {Count} stations — cache updated (no output directory configured).", gbfs.StationCount);
        else
            logger.LogInformation("GBFS refresh: {Count} stations — cache and files updated in {OutputDir}.", gbfs.StationCount, outputDir);
    }

    /// <summary>
    /// Applies an already-exported GBFS result to the cache and the three JSON files atomically.
    /// Used by the full rebuild worker so there is one GBFS code path; mobility polling uses
    /// <see cref="RefreshAsync"/> which exports first.
    /// </summary>
    public async Task ApplyAsync(GbfsExportResult gbfs, DateTime builtAtUtc, CancellationToken ct)
    {
        cache.SetGbfs(gbfs.SystemInformation, gbfs.StationInformation, gbfs.StationStatus, builtAtUtc);
        await WriteFilesAsync(gbfs.SystemInformation, gbfs.StationInformation, gbfs.StationStatus, ct);
    }

    /// <summary>
    /// Writes GBFS JSON to disk atomically so OTP never reads a half-written file. Does not touch
    /// <c>gtfs.zip</c>. Skips file writes if <c>OutputDirectory</c> is empty, mirroring the worker.
    /// </summary>
    public async Task WriteFilesAsync(string systemInformation, string stationInformation, string stationStatus, CancellationToken ct)
    {
        var outputDir = options.Value.Export.OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDir))
            return;

        Directory.CreateDirectory(outputDir);
        await WriteAtomicAsync(Path.Combine(outputDir, "system_information.json"), systemInformation, ct);
        await WriteAtomicAsync(Path.Combine(outputDir, "station_information.json"), stationInformation, ct);
        await WriteAtomicAsync(Path.Combine(outputDir, "station_status.json"), stationStatus, ct);
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var tmpPath = path + ".tmp";
        await File.WriteAllTextAsync(tmpPath, content, ct);
        File.Move(tmpPath, path, overwrite: true);
    }
}
