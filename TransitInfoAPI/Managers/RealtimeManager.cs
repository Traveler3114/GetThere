using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

using TransitRealtime;

namespace TransitInfoAPI.Managers;

public class RealtimeManager
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RealtimeManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, VehicleDto> _vehicleCache = new();

    public RealtimeManager(
        IHttpClientFactory httpFactory,
        ILogger<RealtimeManager> logger,
        IServiceScopeFactory scopeFactory)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task PollAllFeedsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();

        var activeRtFeeds = await db.Feeds
            .Where(f => f.IsActive && f.FeedType == FeedType.GTFSRealtime)
            .Where(f => f.InternalUrl != null || f.ExternalUrl != null)
            .ToListAsync(ct);

        foreach (var feed in activeRtFeeds)
        {
            try
            {
                await PollFeedAsync(feed, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll GTFS-RT feed {FeedId}", feed.FeedId);
            }
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var key in _vehicleCache.Keys)
        {
            if (_vehicleCache.TryGetValue(key, out var v) && v.LastUpdated < cutoff)
                _vehicleCache.TryRemove(key, out _);
        }
    }

    private async Task PollFeedAsync(Feed feed, CancellationToken ct)
    {
        var url = feed.InternalUrl ?? feed.ExternalUrl!;
        var http = _httpFactory.CreateClient("gtfsrt");

        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsByteArrayAsync(ct);

        var feedMessage = FeedMessage.Parser.ParseFrom(body);


        foreach (var entity in feedMessage.Entity)
        {
            if (entity.Vehicle != null)
            {
                var vp = entity.Vehicle;
                if (vp.Position == null || (vp.Position.Latitude == 0 && vp.Position.Longitude == 0)) continue;

                var vehicleId = vp.Vehicle?.Id ?? entity.Id;
                var vehicleDto = new VehicleDto
                {
                    VehicleId = vehicleId,
                    FeedId = feed.FeedId,
                    RouteId = entity.Vehicle?.Trip?.RouteId,
                    TripId = entity.Vehicle?.Trip?.TripId,
                    RouteShortName = null,
                    IsRealtime = true,
                    BlockId = null,
                    Latitude = vp.Position.Latitude,
                    Longitude = vp.Position.Longitude,
                    Bearing = vp.Position.Bearing > 0 ? vp.Position.Bearing : null,
                    LastUpdated = vp.Timestamp > 0
                        ? DateTime.UnixEpoch.AddSeconds(vp.Timestamp)
                        : DateTime.UtcNow
                };

                _vehicleCache[$"{feed.Id}:{vehicleId}"] = vehicleDto;
            }
        }

        // Persist alerts
        using var alertScope = _scopeFactory.CreateScope();
        var db = alertScope.ServiceProvider.GetRequiredService<TransitDbContext>();
        foreach (var entity in feedMessage.Entity.Where(e => e.Alert != null))
        {
            var alert = entity.Alert;
            var cause = alert.Cause.ToString();
            var effect = alert.Effect.ToString();
            var activePeriodStart = alert.ActivePeriod.Count > 0
                ? DateTime.UnixEpoch.AddSeconds((long)alert.ActivePeriod[0].Start)
                : (DateTime?)null;

            var existing = await db.Alerts
                .Where(a => a.FeedId == feed.Id
                    && a.Cause == cause
                    && a.Effect == effect
                    && a.ActivePeriodStart == activePeriodStart)
                .FirstOrDefaultAsync(ct);

            if (existing is null)
            {
                var alertEntity = new Entities.Alert
                {
                    FeedId = feed.Id,
                    HeaderText = alert.HeaderText?.Translation?.FirstOrDefault()?.Text,
                    DescriptionText = alert.DescriptionText?.Translation?.FirstOrDefault()?.Text,
                    Url = alert.Url?.Translation?.FirstOrDefault()?.Text,
                    Cause = cause,
                    Effect = effect,
                    ActivePeriodStart = activePeriodStart,
                    ActivePeriodEnd = alert.ActivePeriod.Count > 0 && alert.ActivePeriod[0].End > 0
                        ? DateTime.UnixEpoch.AddSeconds((long)alert.ActivePeriod[0].End)
                        : null,
                    FetchedAt = DateTime.UtcNow
                };
                db.Alerts.Add(alertEntity);
            }
        }
        await db.SaveChangesAsync(ct);

        var cutoff = DateTime.UtcNow.AddDays(-1);
        var cutoffFetched = DateTime.UtcNow.AddDays(-7);
        await db.Alerts
            .Where(a => a.ActivePeriodEnd < cutoff || a.FetchedAt < cutoffFetched)
            .ExecuteDeleteAsync(ct);
    }

    public Task<List<VehicleDto>> GetVehiclesAsync(
        string? feedId, double? minLat, double? minLon, double? maxLat, double? maxLon, CancellationToken ct)
    {
        var vehicles = _vehicleCache.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(feedId))
            vehicles = vehicles.Where(v => v.FeedId == feedId);

        if (minLat.HasValue && maxLat.HasValue && minLon.HasValue && maxLon.HasValue)
        {
            vehicles = vehicles.Where(v =>
                v.Latitude >= minLat.Value && v.Latitude <= maxLat.Value &&
                v.Longitude >= minLon.Value && v.Longitude <= maxLon.Value);
        }

        return Task.FromResult(vehicles.ToList());
    }

    public async Task<List<AlertDto>> GetAlertsAsync(
        string? stopOnestopId, string? routeOnestopId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();

        var query = db.Alerts.AsQueryable();

        if (!string.IsNullOrEmpty(stopOnestopId))
            query = query.Where(a => a.AffectedStopIds != null && a.AffectedStopIds.Contains(stopOnestopId));

        if (!string.IsNullOrEmpty(routeOnestopId))
            query = query.Where(a => a.AffectedRouteIds != null && a.AffectedRouteIds.Contains(routeOnestopId));

        return await query
            .OrderByDescending(a => a.FetchedAt)
            .Take(50)
            .Select(a => new AlertDto
            {
                Id = a.Id,
                HeaderText = a.HeaderText,
                DescriptionText = a.DescriptionText,
                Url = a.Url,
                Cause = a.Cause,
                Effect = a.Effect,
                ActivePeriodStart = a.ActivePeriodStart,
                ActivePeriodEnd = a.ActivePeriodEnd,
                FetchedAt = a.FetchedAt
            })
            .ToListAsync(ct);
    }
}
