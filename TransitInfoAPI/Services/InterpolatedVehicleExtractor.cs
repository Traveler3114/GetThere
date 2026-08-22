using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Services;

public sealed class InterpolatedVehicleExtractor : IRealtimeExtractor
{
    public string Key => "gtfs-interpolate";
    public string Description => "Interpolates vehicle positions from trip delays and schedule";

    private readonly TransitDbContext _db;
    private readonly RealtimeManager _realtime;
    private readonly ILogger<InterpolatedVehicleExtractor> _logger;
    private readonly TimeZoneInfo _tz;
    private readonly InterpolationTripCache _cache;
    private readonly SecretProtector _secrets;

    public InterpolatedVehicleExtractor(TransitDbContext db, RealtimeManager realtime, ILogger<InterpolatedVehicleExtractor> logger, IConfiguration config, InterpolationTripCache cache, SecretProtector secrets)
    {
        _db = db;
        _realtime = realtime;
        _logger = logger;
        _cache = cache;
        _secrets = secrets;
        var tzId = config.GetValue<string>("Schedule:Timezone", "Europe/Zagreb") ?? "Europe/Zagreb";
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { _tz = TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// Local to UTC, tolerating the spring-forward gap. A schedule can legitimately name a local time
    /// that does not exist on the day the clocks move; ConvertTimeToUtc throws on those. Shifting
    /// forward by the DST delta puts the vehicle at the right instant.
    /// </summary>
    private static DateTime ToUtcSafe(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
            local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    public async Task<List<VehicleResponse>> ExtractAsync(CustomSource source, Feed feed, CancellationToken ct)
    {
        string? sourceFeedId = null;
        var unprotected = _secrets.Unprotect(source.AuthConfig);
        if (!string.IsNullOrWhiteSpace(unprotected))
        {
            try
            {
                using var doc = JsonDocument.Parse(unprotected!);
                if (doc.RootElement.TryGetProperty("sourceFeedId", out var prop))
                    sourceFeedId = prop.GetString();
            }
            catch (JsonException ex) { _logger.LogWarning(ex, "Interpolated extractor for feed {FeedId} has unreadable config", feed.FeedId); }
        }
        if (string.IsNullOrWhiteSpace(sourceFeedId))
        {
            _logger.LogWarning("Interpolated extractor for feed {FeedId} missing sourceFeedId", feed.FeedId);
            return [];
        }

        var sourceFeed = await _db.Feeds.AsNoTracking().FirstOrDefaultAsync(f => f.FeedId == sourceFeedId, ct);
        if (sourceFeed is null) return [];

        if (_realtime.IsStaleFeed(sourceFeed.Id))
            return [];

        var byFeed = _realtime.GetTripUpdatesByFeed();
        if (!byFeed.TryGetValue(sourceFeed.Id, out var updates) || updates.Count == 0)
            return [];

        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _tz);
        var today = DateOnly.FromDateTime(nowLocal);

        var dateKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _cache.EnsureDay(dateKey);

        var activeVersion = await _db.FeedVersions.AsNoTracking()
            .FirstOrDefaultAsync(fv => fv.FeedId == sourceFeed.Id && fv.IsActive, ct);
        if (activeVersion is null) return [];

        var result = new List<VehicleResponse>();
        var processed = 0;
        var capWarned = false;

        foreach (var bundle in updates)
        {
            if (processed >= 500)
            {
                if (!capWarned)
                {
                    _logger.LogWarning("Interpolated extractor for feed {FeedId}: capped at 500 trips, {Remaining} skipped", feed.FeedId, updates.Count - processed);
                    capWarned = true;
                }
                break;
            }
            processed++;

            var tripId = bundle.TripId;
            // Take single delay the bundle carries — uniformly apply
            int delay = 0;
            if (bundle.StopTimeUpdates.Count > 0)
                delay = bundle.StopTimeUpdates.First().DelaySeconds;

            var stops = await GetOrLoadTripStopsAsync(tripId, activeVersion.Id, ct);
            if (stops is null || stops.Count == 0) continue;

            // Compute times with delay, handling overflow past midnight
            // Each stop's arrival/departure are seconds since local midnight of its service day.
            // We convert to local DateTime then to UTC for comparison.
            // The service day the trip started on, not today. An overnight service reported after
            // midnight still belongs to yesterday's schedule, and anchoring it to today put it 24
            // hours out of position.
            var localDate = nowLocal.Date;
            if (!string.IsNullOrWhiteSpace(bundle.StartDate)
                && DateTime.TryParseExact(bundle.StartDate, "yyyyMMdd", CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out var startDay))
            {
                localDate = startDay.Date;
            }

            // Build list of (arrivalUtc, departureUtc, lat, lon)
            var timedStops = new List<(DateTime arrivalUtc, DateTime departureUtc, double lat, double lon)>();
            foreach (var s in stops)
            {
                DateTime arrivalLocal;
                DateTime departureLocal;
                if (s.ArrivalTime >= 86400)
                    arrivalLocal = localDate.AddDays(1).AddSeconds(s.ArrivalTime - 86400 + delay);
                else
                    arrivalLocal = localDate.AddSeconds(s.ArrivalTime + delay);

                if (s.DepartureTime >= 86400)
                    departureLocal = localDate.AddDays(1).AddSeconds(s.DepartureTime - 86400 + delay);
                else
                    departureLocal = localDate.AddSeconds(s.DepartureTime + delay);

                var arrivalUtc = ToUtcSafe(arrivalLocal, _tz);
                var departureUtc = ToUtcSafe(departureLocal, _tz);
                timedStops.Add((arrivalUtc, departureUtc, s.Lat, s.Lon));
            }

            // Find segment bracketing now
            int idx = -1;
            for (var i = 0; i < timedStops.Count - 1; i++)
            {
                if (timedStops[i].departureUtc <= nowUtc && timedStops[i + 1].arrivalUtc > nowUtc)
                {
                    idx = i;
                    break;
                }
            }
            if (idx == -1) continue; // before first departure or after last arrival

            var from = timedStops[idx];
            var to = timedStops[idx + 1];

            var segmentDuration = (to.arrivalUtc - from.departureUtc).TotalSeconds;
            if (segmentDuration <= 0) continue;
            var elapsed = (nowUtc - from.departureUtc).TotalSeconds;
            var fraction = elapsed / segmentDuration;
            fraction = Math.Clamp(fraction, 0, 1);

            var lat = from.lat + fraction * (to.lat - from.lat);
            var lon = from.lon + fraction * (to.lon - from.lon);

            // Bearing heading from segment endpoints
            var bearing = CalculateBearing(from.lat, from.lon, to.lat, to.lon);

            result.Add(new VehicleResponse
            {
                VehicleId = $"interp:{tripId}",
                FeedId = feed.FeedId,
                TripId = tripId,
                RouteId = bundle.RouteId,
                IsRealtime = false,
                Latitude = lat,
                Longitude = lon,
                Bearing = bearing,
                LastUpdated = DateTime.UtcNow
            });
        }

        return result;
    }

    private async Task<IReadOnlyList<InterpolationTripCache.TripStop>?> GetOrLoadTripStopsAsync(string tripId, int feedVersionId, CancellationToken ct)
    {
        var cacheKey = $"{feedVersionId}:{tripId}";
        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        var stops = await _db.StopTimes.AsNoTracking()
            .Where(st => st.Trip.FeedVersionId == feedVersionId && st.Trip.TripId == tripId)
            .OrderBy(st => st.StopSequence)
            .Select(st => new InterpolationTripCache.TripStop(
                st.StopSequence,
                st.ArrivalTime,
                st.DepartureTime,
                st.CanonicalStation != null ? st.CanonicalStation.Latitude : (st.RawStopEntity != null ? st.RawStopEntity.Lat : 0),
                st.CanonicalStation != null ? st.CanonicalStation.Longitude : (st.RawStopEntity != null ? st.RawStopEntity.Lon : 0)
            ))
            .ToListAsync(ct);

        // Filter out stops with no usable coordinates
        var filtered = stops.Where(s => s.Lat != 0 || s.Lon != 0).ToList();
        IReadOnlyList<InterpolationTripCache.TripStop> ro = filtered;
        _cache.Set(cacheKey, ro);
        return ro;
    }

    private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
        var theta = Math.Atan2(y, x);
        var bearing = (theta * 180 / Math.PI + 360) % 360;
        return bearing;
    }
}
