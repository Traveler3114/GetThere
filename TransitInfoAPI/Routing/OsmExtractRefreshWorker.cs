using Microsoft.Extensions.Options;

namespace TransitInfoAPI.Routing;

/// <summary>
/// Nightly caretaker for the OSM extracts: checks Geofabrik's MD5 sidecar and re-downloads only when
/// the region actually changed. Mirrors the polling workers' shape — interval clamp included — and
/// idles when no region is configured.
/// </summary>
public sealed class OsmExtractRefreshWorker : BackgroundService
{
    private readonly ILogger<OsmExtractRefreshWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RoutingOptions> _options;

    public OsmExtractRefreshWorker(
        ILogger<OsmExtractRefreshWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<RoutingOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_options.CurrentValue.OsmExtract.Regions.Count == 0)
        {
            _logger.LogInformation("OSM extract refresher idle — no Routing:OsmExtract:Regions configured");
            return;
        }

        var intervalHours = OsmRefreshInterval.Hours(
            _options.CurrentValue.OsmExtract.RefreshIntervalHours, 24, _logger, "Routing:OsmExtract:RefreshIntervalHours");
        _logger.LogInformation("OSM extract refresher started with {Interval} hour(s) interval", intervalHours);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var downloader = scope.ServiceProvider.GetRequiredService<OsmExtractDownloader>();
                var results = await downloader.RefreshAllAsync(ct);
                foreach (var result in results)
                    _logger.LogInformation("OSM extract [{Region}] changed={Changed} note={Note} path={Path}",
                        result.Region, result.Changed, result.Note ?? "-", result.Path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "OSM extract refresh cycle failed");
            }

            await Task.Delay(TimeSpan.FromHours(
                OsmRefreshInterval.Hours(_options.CurrentValue.OsmExtract.RefreshIntervalHours, 24, _logger,
                    "Routing:OsmExtract:RefreshIntervalHours")), ct);
        }
    }
}

internal static class OsmRefreshInterval
{
    /// <summary>Clamps the configured interval to [1, 168] hours; raises nothing below the floor.</summary>
    public static double Hours(double hours, double floor, ILogger logger, string key)
    {
        if (double.IsNaN(hours) || hours < floor)
        {
            logger.LogWarning("{Key} is {Value}, clamping to {Floor} — the refresher would otherwise hammer Geofabrik",
                key, hours, floor);
            return floor;
        }
        return Math.Min(hours, 168);
    }
}