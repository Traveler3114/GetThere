using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Routing.Export;

namespace TransitInfoAPI.Routing;

/// <summary>
/// Rebuilds the cached routing artifacts whenever a feed version activates (or an OSM refresh is
/// signalled). Runs off the import path so activation is never blocked on an export, and creates its
/// own scope per rebuild because the exporters are scoped (they hold a <c>TransitDbContext</c>).
/// <para>
/// This is where the OTP graph build is triggered in deployment; wiring the rebuild to activation
/// from the start is a correctness requirement, not a later optimisation — an activated ZET refresh
/// whose graph is never rebuilt serves a stale (eventually empty) network.
/// </para>
/// </summary>
public sealed class RoutingRebuildWorker(
    GraphRebuildSignal signal,
    IServiceScopeFactory scopeFactory,
    RoutingExportCache cache,
    IOptions<RoutingOptions> options,
    ILogger<RoutingRebuildWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Build once on startup so the bundle is ready immediately — otherwise the cache is empty
        // (and the export endpoints 404) until the next feed activation, which may be an hour away.
        try
        {
            await RebuildAsync("startup", stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Initial routing artifact build failed");
        }

        await foreach (var reason in signal.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RebuildAsync(reason, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed rebuild must not take the worker down — the next activation retries.
                logger.LogError(ex, "Routing artifact rebuild failed ({Reason})", reason);
            }
        }
    }

    private async Task RebuildAsync(string reason, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var gtfsExporter = scope.ServiceProvider.GetRequiredService<GtfsBundleExporter>();
        var gbfsExporter = scope.ServiceProvider.GetRequiredService<GbfsExporter>();

        var gtfs = await gtfsExporter.ExportAsync(ct);
        var gbfs = await gbfsExporter.ExportAsync(ct);

        var builtAt = DateTime.UtcNow;
        cache.Set(new RoutingExportCache.Artifacts(
            GtfsZip: gtfs.GtfsZip,
            GbfsSystemInformation: gbfs.SystemInformation,
            GbfsStationInformation: gbfs.StationInformation,
            GbfsStationStatus: gbfs.StationStatus,
            BuiltAtUtc: builtAt,
            DroppedStopTimes: gtfs.Resolution.TotalDropped));

        logger.LogInformation(
            "Rebuilt routing artifacts ({Reason}): {RawStops} raw stops, {Routes} routes, {Docks} mobility docks, {Dropped} dropped stop times, {SkippedTrips} trips without a route.",
            reason, gtfs.RawStopCount, gtfs.RouteCount, gbfs.StationCount, gtfs.Resolution.TotalDropped, gtfs.TripsSkippedNoRoute);

        var outputDir = options.Value.Export.OutputDirectory;
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "gtfs.zip"), gtfs.GtfsZip, ct);
            // Reuse the GBFS file-write path so there is one atomic implementation.
            var gbfsRefresh = scope.ServiceProvider.GetRequiredService<GbfsRefreshService>();
            await gbfsRefresh.WriteFilesAsync(gbfs.SystemInformation, gbfs.StationInformation, gbfs.StationStatus, ct);
            logger.LogInformation("Wrote routing artifacts to {OutputDir}", outputDir);
        }

        // TODO(Step 4 hosting): once the OTP endpoint is configured (Routing:Otp:Endpoint), kick an
        // OTP graph build against the refreshed bundle here.
    }
}
