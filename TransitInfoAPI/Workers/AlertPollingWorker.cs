using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Workers;

public sealed class AlertPollingWorker : BackgroundService
{
    private readonly ILogger<AlertPollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public AlertPollingWorker(ILogger<AlertPollingWorker> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var initialDelay = PollingInterval.InitialDelaySeconds(20, 20, _logger, "Alerts:InitialDelaySeconds");
        _logger.LogInformation("Alert polling worker started with {Delay}s initial delay", initialDelay.TotalSeconds);
        await Task.Delay(initialDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            List<Feed> sources;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
                var now = DateTime.UtcNow;
                sources = await db.Feeds
                    .Include(f => f.AlertSource)
                    .Where(f => f.IsActive && f.FeedType == FeedType.AlertSource && f.AlertSourceId != null)
                    .ToListAsync(ct);

                // Per-source cadence: a source polled more recently than its own interval is skipped this tick.
                // The 30s slack is load-bearing. LastRunAt is stamped after the fetch, so without it
                // a source is always a few seconds short of its own cutoff on the tick it is due and
                // gets skipped until the next one — halving every configured interval.
                sources = sources
                    .Where(f => f.AlertSource!.LastRunAt is null
                             || f.AlertSource.LastRunAt <= now.AddMinutes(-f.AlertSource.IntervalMinutes).AddSeconds(30))
                    .ToList();
            }
            if (sources.Count > 0)
                _logger.LogInformation("Polling {Count} alert sources", sources.Count);

            foreach (var source in sources)
            {
                try
                {
                    await PollSourceAsync(source, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to poll alert source {SourceKey}", source.AlertSource!.SourceKey);
                }
            }

            // Refresh road cache so PlanController sees fresh HAK geometry without extra DB hit
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var roadCache = scope.ServiceProvider.GetRequiredService<RoadAlertCache>();
                await roadCache.RefreshAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to refresh road alert cache");
            }

            // Ticks once a minute; the per-source IntervalMinutes filter above decides who actually
            // runs. A 15-minute tick made IntervalMinutes a floor of 15 rather than a setting.
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    private async Task PollSourceAsync(Feed feed, CancellationToken ct)
    {
        var source = feed.AlertSource!;
        using var scope = _scopeFactory.CreateScope();
        var extractor = scope.ServiceProvider.GetRequiredService<AlertSourceExtractor>();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();

        List<ExtractedRow> rows = [];
        List<string> warnings = [];
        string? error = null;

        try
        {
            (rows, warnings) = await extractor.ExtractAsync(source, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            error = ex.Message;
            _logger.LogWarning(ex, "Alert source {SourceKey} failed to extract", source.SourceKey);
        }

        // Recorded whatever happened. A source that 404s is the failure the admin page is for, and
        // it used to leave no trace at all because this ran only on the success path.
        var tracked = await db.AlertSources.FirstOrDefaultAsync(a => a.Id == source.Id, ct);
        if (tracked is not null)
        {
            tracked.LastRunAt = DateTime.UtcNow;
            // Null rather than 0 on failure: 0 means "fetched fine, selector matched nothing", which
            // the console highlights as drift. A failed fetch is a different fault.
            tracked.LastItemCount = error is null ? rows.Count : null;
            tracked.LastError = error ?? (warnings.Count > 0 ? string.Join("; ", warnings) : null);
            if (tracked.LastError is { Length: > 1024 })
                tracked.LastError = tracked.LastError[..1024];
            await db.SaveChangesAsync(ct);
        }

        if (error is not null) return;

        // Selectors drift. A source that yields nothing keeps its existing alerts rather than having
        // them swept, because a layout change must not read as "the disruption ended".
        if (rows.Count == 0)
        {
            _logger.LogWarning("Alert source {SourceKey} yielded 0 items overall", source.SourceKey);
            return;
        }

        await UpsertAlertsAsync(feed, rows, ct);
    }

    private async Task UpsertAlertsAsync(Feed feed, List<ExtractedRow> rows, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
        var matcherLogger = scope.ServiceProvider.GetRequiredService<ILogger<AlertRouteMatcher>>();
        var matcher = new AlertRouteMatcher(db, matcherLogger);

        var source = feed.AlertSource!;
        int? operatorId = feed.OperatorId;

        var existing = await db.Alerts.Where(a => a.SourceKey != null && a.SourceKey.StartsWith(source.SourceKey + ":")).ToListAsync(ct);
        var byKey = existing.ToDictionary(a => a.SourceKey!);

        var now = DateTime.UtcNow;
        var seenKeys = new HashSet<string>();
        var toAdd = new List<Entities.Alert>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = AlertSourceMapper.BuildSourceKey(source.SourceKey, row, i);
            seenKeys.Add(key);

            var (title, description, linkRaw, dateRaw, category) = AlertSourceMapper.ExtractCommon(row);
            if (string.IsNullOrWhiteSpace(title))
                title = row.TryGetValue("Title", out var t) ? t?.ToString() : "Untitled";

            var link = AlertSourceMapper.ResolveUrl(linkRaw, source.Url.Split(';')[0]);
            var severity = AlertSourceMapper.MapSeverity(category, title, description);
            var dateParsed = AlertSourceMapper.ParseDate(dateRaw);
            var (lat, lon, geoJson) = AlertSourceMapper.ExtractGeometry(row);
            // For Road alerts, geometry was extracted; for Transit alerts lat/lon stays null.

            // For HAK sources, title fallback to description category
            if (source.Kind.Equals("Road", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(title))
                title = description ?? category ?? "Road disruption";

            // Road alerts (HAK) carry no operator and describe roads, not transit lines. Matching them
            // would compare a road notice against every operator's route short names and attach an
            // arbitrary one, which would then reach OTP as a disruption on an unrelated route.
            var matchedRouteIds = source.Kind.Equals("Road", StringComparison.OrdinalIgnoreCase)
                ? null
                : await matcher.MatchAsync(title, description, operatorId, ct);
            var effect = AlertSourceMapper.DetermineEffect(title, description);

            if (byKey.TryGetValue(key, out var existingAlert))
            {
                existingAlert.HeaderText = title;
                existingAlert.DescriptionText = description;
                existingAlert.Url = link;
                existingAlert.SourceUrl = link;
                existingAlert.FetchedAt = now;
                existingAlert.SourceKey = key;
                existingAlert.Kind = source.Kind;
                existingAlert.Severity = severity;
                existingAlert.Latitude = lat;
                existingAlert.Longitude = lon;
                existingAlert.GeometryGeoJson = geoJson;
                existingAlert.MatchedRouteIds = matchedRouteIds;
                existingAlert.Cause = "UNKNOWN_CAUSE";
                existingAlert.Effect = effect;
                existingAlert.ActivePeriodStart = dateParsed ?? existingAlert.ActivePeriodStart ?? now;
                existingAlert.FeedId = feed.Id;
                // Keep OperatorId if already set or use inferred
                if (operatorId.HasValue) existingAlert.OperatorId = operatorId;
            }
            else
            {
                var alert = new Entities.Alert
                {
                    FeedId = feed.Id,
                    OperatorId = operatorId,
                    HeaderText = title,
                    DescriptionText = description,
                    Url = link,
                    SourceUrl = link,
                    Cause = "UNKNOWN_CAUSE",
                    Effect = effect,
                    ActivePeriodStart = dateParsed ?? now,
                    ActivePeriodEnd = null,
                    FetchedAt = now,
                    CreatedAt = now,
                    Kind = source.Kind,
                    SourceKey = key,
                    Severity = severity,
                    Latitude = lat,
                    Longitude = lon,
                    GeometryGeoJson = geoJson,
                    MatchedRouteIds = matchedRouteIds
                };
                toAdd.Add(alert);
            }
        }

        if (toAdd.Count > 0)
            db.Alerts.AddRange(toAdd);

        // Sweep: remove alerts of this source no longer present
        var toRemove = existing.Where(a => !seenKeys.Contains(a.SourceKey!)).ToList();
        if (toRemove.Count > 0)
            db.Alerts.RemoveRange(toRemove);

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Alert source {SourceKey}: upserted {Added} new, updated {Updated}, removed {Removed}", source.SourceKey, toAdd.Count, existing.Count - toRemove.Count, toRemove.Count);
    }


}
