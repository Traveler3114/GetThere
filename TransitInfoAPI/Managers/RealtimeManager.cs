using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Workers;

using TransitRealtime;

namespace TransitInfoAPI.Managers;

public class RealtimeManager
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RealtimeManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Services.FeedSourceFactory _feedSourceFactory;
    private readonly int _vehicleStaleCutoffMinutes;
    private readonly int _maxFailuresBeforeDeactivate;
    // In-memory only — does not survive restart. Acceptable: high churn, low value after restart.
    // Revisit for Phase 2 multi-instance deployment.
    private readonly ConcurrentDictionary<string, VehicleResponse> _vehicleCache = new();
    private readonly Dictionary<int, int> _feedFailureCounts = [];
    private readonly object _failureLock = new();

    private record StopTimeUpdateData(int DelaySeconds, long? EstimatedTimeUnix);

    private volatile ConcurrentDictionary<string, ConcurrentDictionary<int, StopTimeUpdateData>> _tripUpdateCache = new();

    public RealtimeManager(
        IHttpClientFactory httpFactory,
        ILogger<RealtimeManager> logger,
        IServiceScopeFactory scopeFactory,
        Services.FeedSourceFactory feedSourceFactory,
        Microsoft.Extensions.Options.IOptions<RealtimePollingOptions> options)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _feedSourceFactory = feedSourceFactory;
        _vehicleStaleCutoffMinutes = options.Value.VehicleStaleCutoffMinutes;
        _maxFailuresBeforeDeactivate = options.Value.MaxConsecutiveFailuresBeforeDeactivate;
    }

    public async Task PollAllFeedsAsync(CancellationToken ct)
    {
        List<Feed> activeRtFeeds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
            activeRtFeeds = await db.Feeds
                .Where(f => f.IsActive && f.FeedType == FeedType.GTFSRealtime)
                .Where(f => f.Url != null)
                .ToListAsync(ct);
        }

        _logger.LogInformation("Polling {Count} active GTFS-RT feeds", activeRtFeeds.Count);

        foreach (var feed in activeRtFeeds)
        {
            try
            {
                await PollFeedAsync(feed, ct);
                lock (_failureLock) _feedFailureCounts.Remove(feed.Id);
                _logger.LogDebug("Feed {FeedId} polled successfully", feed.FeedId);
            }
            catch (Exception ex)
            {
                int count;
                lock (_failureLock)
                {
                    _feedFailureCounts.TryGetValue(feed.Id, out count);
                    count++;
                    _feedFailureCounts[feed.Id] = count;
                }
                _logger.LogWarning(ex, "Failed to poll GTFS-RT feed {FeedId} ({FailCount} consecutive failures)", feed.FeedId, count);

                if (count >= _maxFailuresBeforeDeactivate)
                {
                    _logger.LogWarning("Auto-deactivating GTFS-RT feed {FeedId} after {Count} consecutive failures", feed.FeedId, count);
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
                        var dbFeed = await db.Feeds.FindAsync([feed.Id], ct);
                        if (dbFeed is not null)
                        {
                            dbFeed.IsActive = false;
                            await db.SaveChangesAsync(ct);
                        }
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "Failed to deactivate GTFS-RT feed {FeedId}", feed.FeedId);
                    }
                    lock (_failureLock) _feedFailureCounts.Remove(feed.Id);
                }
            }
        }

        // Vehicle stale cutoff matches realtime poll interval. Move to per-feed config if needed.
        var cutoff = DateTime.UtcNow.AddMinutes(-_vehicleStaleCutoffMinutes);
        foreach (var key in _vehicleCache.Keys)
        {
            if (_vehicleCache.TryGetValue(key, out var v) && v.LastUpdated < cutoff)
                _vehicleCache.TryRemove(key, out _);
        }
    }

    private async Task PollFeedAsync(Feed feed, CancellationToken ct)
    {
        var source = _feedSourceFactory.Resolve(feed);
        var result = await source.FetchDataAsync(feed, ct);

        var feedMessage = FeedMessage.Parser.ParseFrom(new MemoryStream(result.Data));

        var tripUpdates = new ConcurrentDictionary<string, ConcurrentDictionary<int, StopTimeUpdateData>>();
        var alerts = new List<FeedEntity>();

        foreach (var entity in feedMessage.Entity)
        {
            if (entity.Vehicle != null)
            {
                var vp = entity.Vehicle;
                if (vp.Position == null || (vp.Position.Latitude == 0 && vp.Position.Longitude == 0)) continue;

                var vehicleId = vp.Vehicle?.Id ?? entity.Id;
                var vehicleDto = new VehicleResponse
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
                    Bearing = vp.Position.HasBearing ? vp.Position.Bearing : null,
                    LastUpdated = vp.Timestamp > 0
                        ? DateTime.UnixEpoch.AddSeconds(vp.Timestamp)
                        : DateTime.UtcNow
                };

                _vehicleCache[$"{feed.Id}:{vehicleId}"] = vehicleDto;
            }

            if (entity.TripUpdate != null)
            {
                var tu = entity.TripUpdate;
                var tripId = tu.Trip?.TripId;
                if (string.IsNullOrEmpty(tripId)) continue;

                var stopUpdates = new ConcurrentDictionary<int, StopTimeUpdateData>();
                foreach (var stu in tu.StopTimeUpdate)
                {
                    var delay = stu.Departure?.Delay ?? stu.Arrival?.Delay;
                    var time = stu.Departure?.Time ?? stu.Arrival?.Time ?? 0;
                    if (delay.HasValue || time > 0)
                        stopUpdates[(int)stu.StopSequence] = new StopTimeUpdateData(delay ?? 0, time > 0 ? time : null);
                }

                if (stopUpdates.Count > 0)
                    tripUpdates[tripId] = stopUpdates;
            }

            if (entity.Alert != null)
                alerts.Add(entity);
        }

        Interlocked.Exchange(ref _tripUpdateCache, new ConcurrentDictionary<string, ConcurrentDictionary<int, StopTimeUpdateData>>(tripUpdates));

        // Alerts persisted because they carry reference value across restarts (active disruptions).
        // Vehicle positions are ephemeral and remain in-memory only.
        // Persist alerts
        try
        {
            using var alertScope = _scopeFactory.CreateScope();
            var db = alertScope.ServiceProvider.GetRequiredService<TransitDbContext>();

            var existingAlerts = await db.Alerts
                .Where(a => a.FeedId == feed.Id)
                .ToListAsync(ct);
            var existingByKey = existingAlerts
                .GroupBy(a => (a.Cause, a.Effect, a.ActivePeriodStart, a.HeaderText, a.AffectedRouteIds, a.AffectedStopIds, a.AffectedTripIds, a.AffectedAgencyIds))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var entity in alerts)
            {
                var alert = entity.Alert;
                var cause = alert.Cause.ToString();
                var effect = alert.Effect.ToString();
                var activePeriodStart = alert.ActivePeriod.Count > 0
                    ? DateTime.UnixEpoch.AddSeconds((long)alert.ActivePeriod[0].Start)
                    : (DateTime?)null;
                var headerText = alert.HeaderText?.Translation?.FirstOrDefault()?.Text;

                var affectedStopIds = string.Join(",", alert.InformedEntity
                    .Where(e => e.HasStopId).Select(e => e.StopId));
                var affectedRouteIds = string.Join(",", alert.InformedEntity
                    .Where(e => e.HasRouteId).Select(e => e.RouteId));
                var affectedTripIds = string.Join(",", alert.InformedEntity
                    .Where(e => e.Trip != null && !string.IsNullOrEmpty(e.Trip.TripId)).Select(e => e.Trip.TripId));
                var affectedAgencyIds = string.Join(",", alert.InformedEntity
                    .Where(e => e.HasAgencyId).Select(e => e.AgencyId));

                var key = (cause, effect, activePeriodStart, headerText, affectedRouteIds, affectedStopIds, affectedTripIds, affectedAgencyIds);

                if (existingByKey.TryGetValue(key, out var existing))
                {
                    existing.FetchedAt = DateTime.UtcNow;
                }
                else
                {
                    db.Alerts.Add(new Entities.Alert
                    {
                        FeedId = feed.Id,
                        HeaderText = headerText,
                        DescriptionText = alert.DescriptionText?.Translation?.FirstOrDefault()?.Text,
                        Url = alert.Url?.Translation?.FirstOrDefault()?.Text,
                        Cause = cause,
                        Effect = effect,
                        ActivePeriodStart = activePeriodStart,
                        ActivePeriodEnd = alert.ActivePeriod.Count > 0 && alert.ActivePeriod[0].End > 0
                            ? DateTime.UnixEpoch.AddSeconds((long)alert.ActivePeriod[0].End)
                            : null,
                        FetchedAt = DateTime.UtcNow,
                        AffectedStopIds = affectedStopIds,
                        AffectedRouteIds = affectedRouteIds,
                        AffectedTripIds = affectedTripIds,
                        AffectedAgencyIds = affectedAgencyIds
                    });
                }
            }
            await db.SaveChangesAsync(ct);

            var cutoff = DateTime.UtcNow.AddDays(-7);
            await db.Alerts
                .Where(a => a.ActivePeriodEnd != null && a.ActivePeriodEnd < cutoff)
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist alerts for feed {FeedId}", feed.FeedId);
        }
    }

    public (int? DelaySeconds, DateTime? EstimatedDeparture) GetStopDelay(
        string tripId, int stopSequence, DateTime scheduledDeparture)
    {
        if (_tripUpdateCache.TryGetValue(tripId, out var stops) &&
            stops.TryGetValue(stopSequence, out var data))
        {
            if (data.EstimatedTimeUnix.HasValue)
                return (data.DelaySeconds, DateTime.UnixEpoch.AddSeconds(data.EstimatedTimeUnix.Value));
            return (data.DelaySeconds, scheduledDeparture + TimeSpan.FromSeconds(data.DelaySeconds));
        }
        return (null, null);
    }

    public Task<List<VehicleResponse>> GetVehiclesAsync(
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

    public async Task<List<AlertResponse>> GetAlertsAsync(
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
            .Select(a => new AlertResponse
            {
                Id = a.Id,
                HeaderText = a.HeaderText,
                DescriptionText = a.DescriptionText,
                Url = a.Url,
                Cause = a.Cause,
                Effect = a.Effect,
                ActivePeriodStart = a.ActivePeriodStart,
                ActivePeriodEnd = a.ActivePeriodEnd,
                FetchedAt = a.FetchedAt,
                AffectedStopIds = a.AffectedStopIds,
                AffectedRouteIds = a.AffectedRouteIds,
                AffectedTripIds = a.AffectedTripIds,
                AffectedAgencyIds = a.AffectedAgencyIds
            })
            .ToListAsync(ct);
    }

    public void UpdateVehicleCache(string feedId, VehicleResponse vehicle)
    {
        var key = $"{feedId}:{vehicle.VehicleId}";
        _vehicleCache[key] = vehicle;
    }

    public void UpdateTripUpdate(string tripId, int stopSequence, int? delaySeconds)
    {
        var tripCache = _tripUpdateCache;
        if (!tripCache.TryGetValue(tripId, out var stops))
        {
            stops = new ConcurrentDictionary<int, StopTimeUpdateData>();
            tripCache[tripId] = stops;
        }
        stops[stopSequence] = new StopTimeUpdateData(delaySeconds ?? 0, null);
    }
}
