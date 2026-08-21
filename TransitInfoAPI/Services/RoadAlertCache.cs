using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Services;

public sealed class RoadAlertCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RoadAlertCache> _logger;
    private volatile IReadOnlyList<Alert> _cached = [];
    private DateTime _lastRefresh = DateTime.MinValue;

    public RoadAlertCache(IServiceScopeFactory scopeFactory, ILogger<RoadAlertCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
            var now = DateTime.UtcNow;
            var alerts = await db.Alerts.AsNoTracking()
                .Where(a => a.Kind == "Road" && a.GeometryGeoJson != null
                         && (a.ActivePeriodStart == null || a.ActivePeriodStart <= now)
                         && (a.ActivePeriodEnd == null || a.ActivePeriodEnd >= now))
                .ToListAsync(ct);
            _cached = alerts;
            _lastRefresh = DateTime.UtcNow;
            _logger.LogInformation("RoadAlertCache refreshed with {Count} active road alerts", alerts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh RoadAlertCache");
        }
    }

    public IReadOnlyList<Alert> GetCached() => _cached;

    public async Task<IReadOnlyList<Alert>> GetAsync(CancellationToken ct = default)
    {
        // Deliberately not "|| _cached.Count == 0": with no active road alerts — the normal state
        // until HAK credentials are configured — that condition is always true, so every /plan
        // request would hit the database on the hot path. _lastRefresh starts at MinValue, so a
        // cold start still refreshes on the first call.
        if ((DateTime.UtcNow - _lastRefresh).TotalMinutes > 2)
            await RefreshAsync(ct);
        return _cached;
    }
}
