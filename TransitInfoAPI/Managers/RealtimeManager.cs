using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;
using TransitInfoAPI.Workers;

using TransitRealtime;

namespace TransitInfoAPI.Managers;

public class RealtimeManager
{
    private readonly ILogger<RealtimeManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExternalFeedSource _externalFeedSource;
    private readonly int _vehicleStaleCutoffMinutes;
    private readonly int _maxFailuresBeforeDeactivate;
    // In-memory only — does not survive restart. Acceptable: high churn, low value after restart.
    // Revisit for Phase 2 multi-instance deployment.
    private readonly ConcurrentDictionary<string, VehicleResponse> _vehicleCache = new();
    private readonly Dictionary<int, int> _feedFailureCounts = [];
    private readonly object _failureLock = new();

    private record StopTimeUpdateData(int DelaySeconds, long? EstimatedTimeUnix);

    private record TripUpdateBundle(
        Dictionary<int, StopTimeUpdateData> BySequence,
        Dictionary<string, StopTimeUpdateData> ByStopId,
        string? RouteId,
        int? DirectionId,
        string? StartTime);

    private volatile ConcurrentDictionary<string, TripUpdateBundle> _tripUpdateCache = new();

    /// <summary>
    /// Last known good trip updates per feed. Keeping them separate is what lets a feed that fails a
    /// poll retain its previous data instead of being blanked from the flattened cache.
    /// </summary>
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, TripUpdateBundle>> _tripUpdatesByFeed = new();

    public RealtimeManager(
        ILogger<RealtimeManager> logger,
        IServiceScopeFactory scopeFactory,
        ExternalFeedSource externalFeedSource,
        Microsoft.Extensions.Options.IOptions<RealtimePollingOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _externalFeedSource = externalFeedSource;
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
                .Where(f => f.IsActive && f.FeedType == FeedType.GTFSRealtime && f.Url != null)
                .ToListAsync(ct);
        }

        _logger.LogInformation("Polling {Count} active GTFS-RT feeds", activeRtFeeds.Count);

        await Parallel.ForEachAsync(activeRtFeeds, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (feed, innerCt) =>
        {
            try
            {
                var feedUpdates = await PollFeedAsync(feed, innerCt);
                lock (_failureLock) _feedFailureCounts.Remove(feed.Id);
                _logger.LogDebug("Feed {FeedId} polled successfully", feed.FeedId);

                // Results are held per feed so that one feed failing does not discard its previously
                // good data: the cache used to be rebuilt from this cycle's successes alone, so a
                // single transient failure blanked that operator's realtime view until the next
                // successful poll.
                _tripUpdatesByFeed[feed.Id] = feedUpdates;
            }
            catch (Exception ex) when (!innerCt.IsCancellationRequested)
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
                        var dbFeed = await db.Feeds.FindAsync([feed.Id], innerCt);
                        if (dbFeed is not null)
                        {
                            dbFeed.IsActive = false;
                            await db.SaveChangesAsync(innerCt);
                        }
                    }
                    catch (Exception inner) when (!innerCt.IsCancellationRequested)
                    {
                        _logger.LogError(inner, "Failed to deactivate GTFS-RT feed {FeedId}", feed.FeedId);
                    }
                    lock (_failureLock) _feedFailureCounts.Remove(feed.Id);
                    _tripUpdatesByFeed.TryRemove(feed.Id, out _);
                }
            }
        });

        // Drop feeds that are no longer active, then flatten what remains into the lookup the
        // readers use.
        var activeFeedIds = activeRtFeeds.Select(f => f.Id).ToHashSet();
        foreach (var goneFeedId in _tripUpdatesByFeed.Keys.Where(id => !activeFeedIds.Contains(id)).ToList())
            _tripUpdatesByFeed.TryRemove(goneFeedId, out _);

        var merged = new ConcurrentDictionary<string, TripUpdateBundle>();
        foreach (var feedUpdates in _tripUpdatesByFeed.Values)
        {
            foreach (var kvp in feedUpdates)
                merged[kvp.Key] = kvp.Value;
        }

        Interlocked.Exchange(ref _tripUpdateCache, merged);

        // Vehicle stale cutoff matches realtime poll interval. Move to per-feed config if needed.
        //
        // KNOWN GAP: this prunes on LastUpdated, which is the operator's own `vp.Timestamp` (see
        // PollFeedAsync) and is therefore untrusted. A feed publishing a timestamp in the future
        // produces entries that are never older than the cutoff and so are never evicted — and the
        // cache key includes the operator-supplied vehicle id, so the number of such entries is
        // bounded by what the feed chooses to emit rather than by how many vehicles exist. A broken
        // producer with a clock fault does this as readily as a hostile one.
        //
        // The fix is to clamp on ingest — a vehicle position cannot be from the future, so
        // LastUpdated should be `min(vp.Timestamp, UtcNow)` — but that changes what the map displays
        // for every feed whose clock runs fast, and there is no way to check the effect on real
        // feeds from here. Recorded rather than applied.
        var cutoff = DateTime.UtcNow.AddMinutes(-_vehicleStaleCutoffMinutes);
        foreach (var key in _vehicleCache.Keys)
        {
            if (_vehicleCache.TryGetValue(key, out var v) && v.LastUpdated < cutoff)
                _vehicleCache.TryRemove(key, out _);
        }
    }

    private async Task<ConcurrentDictionary<string, TripUpdateBundle>> PollFeedAsync(Feed feed, CancellationToken ct)
    {
        var result = await _externalFeedSource.FetchDataAsync(feed, ct);

        var feedMessage = FeedMessage.Parser.ParseFrom(new MemoryStream(result.Data));

        ConcurrentDictionary<string, TripUpdateBundle> tripUpdates = [];
        List<FeedEntity> alerts = [];

        int tripUpdateCount = 0;
        int tripUpdateWithStopTimeUpdates = 0;
        int tripUpdateWithDelays = 0;
        var sampleTripIdsWithDelays = new List<string>();

        foreach (var entity in feedMessage.Entity)
        {
            if (entity.Vehicle is not null)
            {
                var vp = entity.Vehicle;
                if (vp.Position is null || (vp.Position.Latitude == 0 && vp.Position.Longitude == 0)) continue;
                if (string.IsNullOrEmpty(vp.Trip?.TripId)) continue;

                var vehicleId = vp.Vehicle?.Id ?? entity.Id;
                var vehicleDto = new VehicleResponse
                {
                    VehicleId = vehicleId,
                    FeedId = feed.FeedId,
                    RouteId = entity.Vehicle?.Trip?.RouteId,
                    TripId = entity.Vehicle?.Trip?.TripId,
                    IsRealtime = true,
                    Latitude = vp.Position.Latitude,
                    Longitude = vp.Position.Longitude,
                    Bearing = vp.Position.HasBearing ? vp.Position.Bearing : null,
                    Speed = vp.Position.HasSpeed ? vp.Position.Speed : null,
                    LastUpdated = vp.Timestamp > 0
                        ? DateTime.UnixEpoch.AddSeconds(vp.Timestamp)
                        : DateTime.UtcNow,
                    OccupancyStatus = vp.HasOccupancyStatus ? vp.OccupancyStatus.ToString() : null,
                    OccupancyPercentage = vp.HasOccupancyPercentage ? (int?)vp.OccupancyPercentage : null,
                    CongestionLevel = vp.HasCongestionLevel ? vp.CongestionLevel.ToString() : null,
                    WheelchairAccessible = vp.Vehicle?.HasWheelchairAccessible == true
                        ? vp.Vehicle.WheelchairAccessible.ToString() : null
                };

                _vehicleCache[$"{feed.Id}:{vehicleId}"] = vehicleDto;
            }

            if (entity.TripUpdate is not null)
            {
                tripUpdateCount++;
                var tu = entity.TripUpdate;
                var tripId = tu.Trip?.TripId;
                if (string.IsNullOrEmpty(tripId)) continue;

                var bySequence = new Dictionary<int, StopTimeUpdateData>();
                var byStopId = new Dictionary<string, StopTimeUpdateData>(StringComparer.Ordinal);

                foreach (var stu in tu.StopTimeUpdate)
                {
                    var delay = stu.Departure?.Delay ?? stu.Arrival?.Delay;
                    var time = stu.Departure?.Time ?? stu.Arrival?.Time ?? 0;
                    if (!delay.HasValue && time <= 0) continue;

                    var data = new StopTimeUpdateData(delay ?? 0, time > 0 ? time : null);

                    // stop_sequence often defaults to 0 when producers only populate stop_id —
                    // store both so lookup can prefer the more reliable field (stop_id).
                    if (stu.StopSequence > 0)
                        bySequence[(int)stu.StopSequence] = data;
                    if (!string.IsNullOrEmpty(stu.StopId))
                        byStopId[stu.StopId] = data;
                }

                if (bySequence.Count > 0 || byStopId.Count > 0)
                {
                    tripUpdateWithStopTimeUpdates++;
                    var hasNonZeroDelay = (bySequence.Values.Any(v => v.DelaySeconds != 0) || byStopId.Values.Any(v => v.DelaySeconds != 0));
                    if (hasNonZeroDelay)
                    {
                        tripUpdateWithDelays++;
                        if (sampleTripIdsWithDelays.Count < 10)
                            sampleTripIdsWithDelays.Add(tripId);
                    }
                    // Extract trip descriptor for fallback matching
                    var tripDesc = tu.Trip;
                    var routeId = tripDesc?.RouteId;
                    var directionId = tripDesc?.HasDirectionId == true ? (int?)tripDesc.DirectionId : null;
                    var startTime = tripDesc?.StartTime;

                    // Debug, not Information: this is one line per trip update, per feed, per poll —
                    // a city feed emits thousands every cycle, which buried everything else in the
                    // log. The IsEnabled guard keeps the string.Join from running when the level is
                    // off, which is the whole cost of the line.
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("RT PARSE: feed={FeedId} trip_id={TripId} routeId={RouteId} directionId={DirectionId} startTime={StartTime} seqCount={SeqCount} stopIdCount={StopIdCount} hasNonZeroDelay={HasDelay} sampleDelays={Delays}",
                            feed.FeedId, tripId, routeId, directionId, startTime, bySequence.Count, byStopId.Count, hasNonZeroDelay,
                            string.Join(",", bySequence.Values.Concat(byStopId.Values).Where(v => v.DelaySeconds != 0).Take(5).Select(v => v.DelaySeconds)));
                    }
                    tripUpdates[tripId] = new TripUpdateBundle(bySequence, byStopId, routeId, directionId, startTime);
                }
                else
                    _logger.LogDebug("TripUpdate for trip {TripId} on feed {FeedId} has neither stop_id nor stop_sequence — unmatchable", tripId, feed.FeedId);
            }

            if (entity.Alert is not null)
                alerts.Add(entity);
        }

        // One summary line per feed per poll. The two lines that used to follow it were debugging
        // scaffolding — a sample of trip ids, and a membership test against a trip id hardcoded from
        // someone's investigation ("0_2_201_2_21154") that was evaluated and logged on every poll of
        // every feed. The per-trip detail they were reaching for is the LogDebug above.
        _logger.LogInformation(
            "Feed {FeedId}: {TripUpdateCount} trip_update entities, {MatchedCount} with stop_time_update data, {DelayCount} with non-zero delays. Sample delay trips: {SampleDelayTrips}",
            feed.FeedId, tripUpdateCount, tripUpdateWithStopTimeUpdates, tripUpdateWithDelays, string.Join(", ", sampleTripIdsWithDelays));

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
                    .Where(e => e.Trip is not null && !string.IsNullOrEmpty(e.Trip.TripId)).Select(e => e.Trip.TripId));
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

            // Two ways an alert becomes stale, and only the first used to be swept: it ended more
            // than a week ago, or it has no end at all and simply stopped appearing in the feed.
            // GTFS-RT alerts routinely carry no ActivePeriod end, so those rows accumulated forever
            // — and every poll re-read the whole set to dedupe against them. FetchedAt is refreshed
            // on each poll that still carries the alert, so it is the "still live" signal.
            var cutoff = DateTime.UtcNow.AddDays(-7);
            await db.Alerts
                .Where(a => a.FeedId == feed.Id
                         && (a.ActivePeriodEnd != null ? a.ActivePeriodEnd < cutoff : a.FetchedAt < cutoff))
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist alerts for feed {FeedId}", feed.FeedId);
        }

        return tripUpdates;
    }

    public (int? DelaySeconds, DateTime? EstimatedDeparture) GetStopDelay(
        string tripId, string? rawStopId, int stopSequence, DateTime scheduledDeparture)
    {
        if (!_tripUpdateCache.TryGetValue(tripId, out var bundle))
            return (null, null);

        // Exact match — safe to trust the absolute EstimatedTimeUnix if present,
        // since it genuinely refers to this stop.
        StopTimeUpdateData? exact = null;
        if (!string.IsNullOrEmpty(rawStopId) && bundle.ByStopId.TryGetValue(rawStopId, out var byId))
            exact = byId;
        else if (bundle.BySequence.TryGetValue(stopSequence, out var bySeq))
            exact = bySeq;

        if (exact is not null)
        {
            return exact.EstimatedTimeUnix.HasValue
                ? (exact.DelaySeconds, DateTime.UnixEpoch.AddSeconds(exact.EstimatedTimeUnix.Value))
                : (exact.DelaySeconds, scheduledDeparture + TimeSpan.FromSeconds(exact.DelaySeconds));
        }

        // No exact match — propagate delay from the nearest preceding stop_sequence
        // per GTFS-RT sparse-update convention. Its absolute EstimatedTimeUnix belongs
        // to a DIFFERENT stop and must never be reused here — only the delay offset
        // is valid to carry forward.
        if (bundle.BySequence.Count > 0)
        {
            var predecessor = bundle.BySequence.Keys
                .Where(seq => seq <= stopSequence)
                .OrderByDescending(seq => seq)
                .FirstOrDefault(-1);

            if (predecessor >= 0)
            {
                var propagated = bundle.BySequence[predecessor];
                return (propagated.DelaySeconds, scheduledDeparture + TimeSpan.FromSeconds(propagated.DelaySeconds));
            }
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

        // The affected-id columns are comma-joined lists, so a bare Contains matched on any
        // substring: an alert affecting stop "1234" was returned for a query about stop "123".
        // Padding both sides with the delimiter pins the match to a whole element.
        if (!string.IsNullOrEmpty(stopOnestopId))
        {
            var needle = "," + stopOnestopId + ",";
            query = query.Where(a => a.AffectedStopIds != null && ("," + a.AffectedStopIds + ",").Contains(needle));
        }

        if (!string.IsNullOrEmpty(routeOnestopId))
        {
            var needle = "," + routeOnestopId + ",";
            query = query.Where(a => a.AffectedRouteIds != null && ("," + a.AffectedRouteIds + ",").Contains(needle));
        }

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

    public bool HasTripUpdate(string tripId) => _tripUpdateCache.ContainsKey(tripId);

    public List<TripUpdateResponse> GetTripUpdates(string? routeId = null)
    {
        var results = new List<TripUpdateResponse>();
        foreach (var (tripId, bundle) in _tripUpdateCache)
        {
            if (routeId is not null && bundle.RouteId != routeId) continue;
            results.Add(BuildTripUpdateResponse(tripId, bundle));
        }
        return results;
    }

    /// <summary>
    /// The current trip updates grouped by the feed that produced them, so a consumer that must know
    /// which feed (and therefore which operator, and which active static version) an update belongs to
    /// can — the flattened <see cref="GetTripUpdates"/> cache has lost that. Used by the GTFS-RT
    /// re-serve exporter to namespace ids to the export bundle.
    /// </summary>
    public IReadOnlyDictionary<int, List<TripUpdateResponse>> GetTripUpdatesByFeed()
    {
        var result = new Dictionary<int, List<TripUpdateResponse>>();
        foreach (var (feedId, updates) in _tripUpdatesByFeed)
        {
            var list = new List<TripUpdateResponse>(updates.Count);
            foreach (var (tripId, bundle) in updates)
                list.Add(BuildTripUpdateResponse(tripId, bundle));
            result[feedId] = list;
        }
        return result;
    }

    private static TripUpdateResponse BuildTripUpdateResponse(string tripId, TripUpdateBundle bundle)
    {
        var stopTimeUpdates = new List<StopTimeUpdateResponse>();
        foreach (var (seq, data) in bundle.BySequence)
            stopTimeUpdates.Add(new StopTimeUpdateResponse { StopSequence = seq, DelaySeconds = data.DelaySeconds, EstimatedTime = data.EstimatedTimeUnix });
        foreach (var (stopId, data) in bundle.ByStopId)
        {
            var existing = stopTimeUpdates.FirstOrDefault(s => s.StopId == stopId);
            if (existing is not null)
                existing.DelaySeconds = data.DelaySeconds;
            else
                stopTimeUpdates.Add(new StopTimeUpdateResponse { StopId = stopId, DelaySeconds = data.DelaySeconds, EstimatedTime = data.EstimatedTimeUnix });
        }

        return new TripUpdateResponse
        {
            TripId = tripId,
            RouteId = bundle.RouteId,
            DirectionId = bundle.DirectionId,
            StartTime = bundle.StartTime,
            StopTimeUpdates = stopTimeUpdates
        };
    }
}
