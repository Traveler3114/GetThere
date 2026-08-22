using Google.Protobuf;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;

using TransitRealtime;

namespace TransitInfoAPI.Routing.Export;

public sealed class AlertGtfsRtExporter
{
    private readonly TransitDbContext _db;
    private readonly ILogger<AlertGtfsRtExporter> _logger;

    public AlertGtfsRtExporter(TransitDbContext db, ILogger<AlertGtfsRtExporter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var alerts = await _db.Alerts.AsNoTracking()
            .Where(a => a.MatchedRouteIds != null && a.MatchedRouteIds != ""
                     && (a.ActivePeriodStart == null || a.ActivePeriodStart <= now)
                     && (a.ActivePeriodEnd == null || a.ActivePeriodEnd >= now))
            .ToListAsync(ct);

        if (alerts.Count == 0)
        {
            // Return empty feed (still valid protobuf)
            var empty = new FeedMessage
            {
                Header = new FeedHeader
                {
                    GtfsRealtimeVersion = "2.0",
                    Incrementality = FeedHeader.Types.Incrementality.FullDataset,
                    Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            };
            return empty.ToByteArray();
        }

        // Resolve CanonicalRoute Id -> OnestopId for namespace
        var allRouteIds = alerts
            .SelectMany(a => (a.MatchedRouteIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .Distinct()
            .ToList();

        var routeOnestop = await _db.CanonicalRoutes.AsNoTracking()
            .Where(r => allRouteIds.Contains(r.Id))
            .Select(r => new { r.Id, r.OnestopId })
            .ToDictionaryAsync(r => r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), r => r.OnestopId, ct);

        var message = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Incrementality = FeedHeader.Types.Incrementality.FullDataset,
                Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        };

        foreach (var alert in alerts)
        {
            var entity = new FeedEntity { Id = alert.SourceKey ?? $"alert-{alert.Id}" };
            var gtfsAlert = new Alert();

            // Active period
            if (alert.ActivePeriodStart.HasValue || alert.ActivePeriodEnd.HasValue)
            {
                var tr = new TimeRange();
                if (alert.ActivePeriodStart.HasValue)
                    tr.Start = (ulong)new DateTimeOffset(alert.ActivePeriodStart.Value).ToUnixTimeSeconds();
                if (alert.ActivePeriodEnd.HasValue)
                    tr.End = (ulong)new DateTimeOffset(alert.ActivePeriodEnd.Value).ToUnixTimeSeconds();
                gtfsAlert.ActivePeriod.Add(tr);
            }

            // Informed entities: one per matched route
            var matched = (alert.MatchedRouteIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var mid in matched)
            {
                if (!routeOnestop.TryGetValue(mid, out var onestop))
                {
                    _logger.LogWarning("Alert {AlertId} matched route {RouteId} has no OnestopId, skipping entity", alert.Id, mid);
                    continue;
                }
                gtfsAlert.InformedEntity.Add(new EntitySelector { RouteId = onestop });
            }
            if (gtfsAlert.InformedEntity.Count == 0)
                continue; // no routable mapping

            // Header / description
            if (!string.IsNullOrWhiteSpace(alert.HeaderText))
            {
                gtfsAlert.HeaderText = new TranslatedString();
                gtfsAlert.HeaderText.Translation.Add(new TranslatedString.Types.Translation { Text = alert.HeaderText });
            }
            if (!string.IsNullOrWhiteSpace(alert.DescriptionText))
            {
                gtfsAlert.DescriptionText = new TranslatedString();
                gtfsAlert.DescriptionText.Translation.Add(new TranslatedString.Types.Translation { Text = alert.DescriptionText });
            }
            if (!string.IsNullOrWhiteSpace(alert.Url) || !string.IsNullOrWhiteSpace(alert.SourceUrl))
            {
                var u = alert.Url ?? alert.SourceUrl;
                gtfsAlert.Url = new TranslatedString();
                gtfsAlert.Url.Translation.Add(new TranslatedString.Types.Translation { Text = u });
            }

            gtfsAlert.Cause = ParseCause(alert.Cause);
            gtfsAlert.Effect = ParseEffect(alert.Effect);

            entity.Alert = gtfsAlert;
            message.Entity.Add(entity);
        }

        return message.ToByteArray();
    }

    private static Alert.Types.Cause ParseCause(string? cause)
    {
        if (string.IsNullOrWhiteSpace(cause)) return Alert.Types.Cause.UnknownCause;
        if (Enum.TryParse<Alert.Types.Cause>(cause, true, out var parsed)) return parsed;
        return Alert.Types.Cause.UnknownCause;
    }

    private static Alert.Types.Effect ParseEffect(string? effect)
    {
        if (string.IsNullOrWhiteSpace(effect)) return Alert.Types.Effect.UnknownEffect;
        // Stored as "DETOUR", "NO_SERVICE", "OTHER_EFFECT"
        if (Enum.TryParse<Alert.Types.Effect>(effect, true, out var parsed)) return parsed;
        // Fallback mapping
        var lower = effect.ToLowerInvariant();
        if (lower.Contains("detour")) return Alert.Types.Effect.Detour;
        if (lower.Contains("no_service")) return Alert.Types.Effect.NoService;
        return Alert.Types.Effect.UnknownEffect;
    }
}
