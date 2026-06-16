using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

using TransitRealtime;

namespace TransitInfoAPI.Services;

public class RealtimeService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RealtimeService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, VehicleDto> _vehicleCache = new();
    private readonly object _alertLock = new();

    public RealtimeService(
        IHttpClientFactory httpFactory,
        ILogger<RealtimeService> logger,
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

            if (entity.Alert != null)
            {
                var alert = entity.Alert;
                lock (_alertLock)
                {
                    // Store in DB via scope later
                }
            }
        }

        // Persist alerts
        using var alertScope = _scopeFactory.CreateScope();
        var db = alertScope.ServiceProvider.GetRequiredService<TransitDbContext>();
        foreach (var entity in feedMessage.Entity.Where(e => e.Alert != null))
        {
            var alert = entity.Alert;
            var existing = await db.Alerts
                .Where(a => a.FeedId == feed.Id)
                .OrderByDescending(a => a.FetchedAt)
                .FirstOrDefaultAsync(ct);

            var headerText = alert.HeaderText?.Translation?.FirstOrDefault()?.Text;
            if (existing?.HeaderText != headerText)
            {
                var alertEntity = new Entities.Alert
                {
                    FeedId = feed.Id,
                    HeaderText = headerText,
                    DescriptionText = alert.DescriptionText?.Translation?.FirstOrDefault()?.Text,
                    Url = alert.Url?.Translation?.FirstOrDefault()?.Text,
                    Cause = alert.Cause.ToString(),
                    Effect = alert.Effect.ToString(),
                    ActivePeriodStart = alert.ActivePeriod.Count > 0
                        ? DateTime.UnixEpoch.AddSeconds((long)alert.ActivePeriod[0].Start)
                        : null,
                    ActivePeriodEnd = alert.ActivePeriod.Count > 0 && alert.ActivePeriod[0].End > 0
                        ? DateTime.UnixEpoch.AddSeconds((long)alert.ActivePeriod[0].End)
                        : null,
                    FetchedAt = DateTime.UtcNow
                };
                db.Alerts.Add(alertEntity);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<List<VehicleDto>> GetVehiclesAsync(
        string? feedId, double? minLat, double? minLon, double? maxLat, double? maxLon, CancellationToken ct)
    {
        var vehicles = _vehicleCache.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(feedId))
            vehicles = vehicles.Where(v => v.VehicleId.StartsWith(feedId + ":"));

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
