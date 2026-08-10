using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class ScheduleManager
{
    private readonly TimeZoneInfo _tz;
    private readonly TransitDbContext _db;
    private readonly RealtimeManager _realtime;
    private readonly ILogger<ScheduleManager> _logger;
    private readonly IMemoryCache _cache;

    public ScheduleManager(TransitDbContext db, RealtimeManager realtime, ILogger<ScheduleManager> logger, IConfiguration config, IMemoryCache cache)
    {
        _db = db;
        _realtime = realtime;
        _logger = logger;
        _cache = cache;
        var tzId = config.GetValue<string>("Schedule:Timezone", "Europe/Zagreb");
        try
        {
            _tz = TimeZoneInfo.FindSystemTimeZoneById(tzId!);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // This is scoped, so an unusable configured id would otherwise throw on every request
            // that touches a schedule — an endless stream of 500s rather than one clear failure.
            // Falling back keeps departures working with a loud, once-per-scope warning.
            _logger.LogError(ex,
                "Schedule:Timezone '{TimeZoneId}' is not a time zone this machine knows; falling back to UTC. Departure times will be wrong until it is corrected.",
                tzId);
            _tz = TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// How many rows to pull before the service-calendar filter runs. That filter cannot be
    /// expressed in the query — validity depends on calendar rows and exception dates for a specific
    /// date — so the read is bounded by a multiple of what the caller asked for rather than exactly.
    /// </summary>
    private const int CalendarFilterOverfetchFactor = 6;

    /// <summary>Ceiling on the over-fetch, so a large <c>count</c> cannot reopen the unbounded read.</summary>
    private const int MaxDepartureScanRows = 2000;

    /// <summary>One scanned stop-time row, before the service-calendar filter decides it counts.</summary>
    private sealed class DepartureRow
    {
        public string TripId { get; init; } = "";
        public int StopSequence { get; init; }
        public string RawStopId { get; init; } = "";
        public int FeedVersionId { get; init; }
        public string ServiceId { get; init; } = "";
        public int DepartureTime { get; init; }
        public string RouteName { get; init; } = "";
        public string Headsign { get; init; } = "";
    }

    public async Task<List<DepartureResponse>> GetDeparturesAsync(
        int canonicalStationId, DateTime from, int count, CancellationToken ct)
    {
        var utcFrom = from.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(from, DateTimeKind.Utc)
            : from.ToUniversalTime();
        var localFrom = TimeZoneInfo.ConvertTimeFromUtc(utcFrom, _tz);

        var today = DateOnly.FromDateTime(localFrom);
        var fromTime = (int)Math.Round(localFrom.TimeOfDay.TotalSeconds);

        var validServices = await GetValidServiceIdsAsync(today, today.DayOfWeek, ct);
        var tomorrow = today.AddDays(1);
        var validServicesTomorrow = await GetValidServiceIdsAsync(tomorrow, tomorrow.DayOfWeek, ct);

        // Ordered and bounded in SQL. This read had no Take at all: it materialised every remaining
        // departure of the service day, across every active feed version, and then kept `count` of
        // them in memory. The (CanonicalStationId, DepartureTime) index exists for exactly this
        // ordering — it just was not being used to limit anything.
        //
        // The service-calendar filter cannot be expressed in the query (validity depends on calendar
        // rows and exception dates for a specific date), so some of what is read gets discarded and
        // a single fixed over-fetch is a guess. Measured against a real ZET station, 60 rows yielded
        // 6 valid departures — a fixed multiplier silently returned fewer than asked for. So the scan
        // widens until it has enough, the source is exhausted, or it reaches the hard cap.
        // The multiply is done in long and clamped low as well as high. The controller bounds count,
        // but this is a public method and a large value used to overflow int here — Math.Min then
        // picked the negative product and Take() threw. Defending at the point of arithmetic means
        // the guard does not depend on every caller remembering the attribute.
        var scanLimit = (int)Math.Clamp((long)count * CalendarFilterOverfetchFactor, 1, MaxDepartureScanRows);
        List<DepartureRow> rawDepartures;
        List<DepartureRow> filteredDepartures;

        while (true)
        {
            rawDepartures = await _db.StopTimes
                .Where(st => st.CanonicalStationId == canonicalStationId)
                .Where(st => st.Trip.FeedVersion.IsActive)
                .Where(st => st.DepartureTime >= fromTime)
                .OrderBy(st => st.DepartureTime)
                .Take(scanLimit)
                .Select(st => new DepartureRow
                {
                    TripId = st.Trip.TripId,
                    StopSequence = st.StopSequence,
                    RawStopId = st.RawStopId,
                    FeedVersionId = st.Trip.FeedVersionId,
                    ServiceId = st.Trip.ServiceId,
                    DepartureTime = st.DepartureTime,
                    RouteName = st.Trip.CanonicalRoute != null
                        ? (st.Trip.CanonicalRoute.ShortName != "" && st.Trip.CanonicalRoute.LongName != ""
                            ? st.Trip.CanonicalRoute.ShortName + " - " + st.Trip.CanonicalRoute.LongName
                            : st.Trip.CanonicalRoute.ShortName != ""
                                ? st.Trip.CanonicalRoute.ShortName
                                : st.Trip.CanonicalRoute.LongName)
                        : (st.Trip.TripShortName ?? ""),
                    // Read from the trip rather than hardcoded to "". This was `Headsign = ""`, so
                    // every departure the map ever rendered showed no destination — while the same
                    // field is projected correctly by GetRouteTripsAsync below.
                    Headsign = st.Trip.TripHeadsign ?? ""
                })
                .ToListAsync(ct);

            filteredDepartures = rawDepartures
                .Where(d => d.DepartureTime >= 86400
                    ? validServicesTomorrow.Contains((d.FeedVersionId, d.ServiceId))
                    : validServices.Contains((d.FeedVersionId, d.ServiceId)))
                .OrderBy(d => d.DepartureTime)
                .Take(count)
                .ToList();

            // Enough results, or the station simply has no more rows to read, or we are at the cap.
            if (filteredDepartures.Count >= count
                || rawDepartures.Count < scanLimit
                || scanLimit >= MaxDepartureScanRows)
                break;

            scanLimit = Math.Min(scanLimit * 4, MaxDepartureScanRows);
        }

        // One line per request, at Debug. This method used to log four Information lines per call
        // plus three per departure from inside the projection — roughly 34 lines for a ten-departure
        // request, on the endpoint the map calls every time someone taps a stop.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Departures for station {StationId} from {LocalFrom}: scanned {Scanned} (cap {Cap}), returned {Returned}",
                canonicalStationId, localFrom, rawDepartures.Count, scanLimit, filteredDepartures.Count);
        }

        // Only a genuine shortfall now: the cap was reached, the source still had more, and we still
        // could not fill the request.
        if (filteredDepartures.Count < count && rawDepartures.Count == scanLimit && scanLimit >= MaxDepartureScanRows)
        {
            _logger.LogWarning(
                "Departure scan for station {StationId} reached its {Cap}-row ceiling and returned {Returned} of {Count} requested — the station's active services are sparse relative to its stop times.",
                canonicalStationId, MaxDepartureScanRows, filteredDepartures.Count, count);
        }

        return filteredDepartures
            .Select(d =>
            {
                var localDate = localFrom.Date;
                var departureTime = d.DepartureTime >= 86400
                    ? localDate.AddDays(1).AddSeconds(d.DepartureTime - 86400)
                    : localDate.AddSeconds(d.DepartureTime);
                var (delay, estimated) = _realtime.GetStopDelay(d.TripId, d.RawStopId, d.StopSequence, departureTime);
                return new DepartureResponse
                {
                    TripId = d.TripId,
                    RouteName = d.RouteName,
                    Headsign = d.Headsign,
                    ScheduledDeparture = departureTime,
                    EstimatedDeparture = estimated,
                    DelaySeconds = delay
                };
            })
            .ToList();
    }

    /// <summary>
    /// Which (feed version, service) pairs run on a given date.
    /// <para>
    /// Cached because this is four queries — active versions, calendars, added and removed exception
    /// dates — and the departures endpoint calls it twice per request, once for today and once for
    /// tomorrow. That was eight uncached queries on the busiest endpoint in the service, each reading
    /// every calendar row for every active feed version.
    /// </para>
    /// <para>
    /// The answer only changes when a feed is imported or a version is activated, so a short absolute
    /// expiry is enough — a newly imported version becomes visible within the window rather than
    /// needing explicit invalidation from the import path.
    /// </para>
    /// </summary>
    private static readonly TimeSpan ServiceCalendarCacheTtl = TimeSpan.FromMinutes(5);

    private async Task<HashSet<(int FeedVersionId, string ServiceId)>> GetValidServiceIdsAsync(
        DateOnly date, DayOfWeek dayOfWeek, CancellationToken ct)
    {
        var cacheKey = $"schedule:services:{date:yyyy-MM-dd}";
        if (_cache.TryGetValue<HashSet<(int, string)>>(cacheKey, out var cached) && cached is not null)
            return cached;

        var computed = await LoadValidServiceIdsAsync(date, dayOfWeek, ct);

        _cache.Set(cacheKey, computed, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ServiceCalendarCacheTtl,
            // Harmless while this service's cache has no SizeLimit, and required the moment one is
            // added — a size-limited cache throws on any entry that does not declare one. GetThereAPI
            // already sets a limit on its cache, so this is the shape to follow.
            Size = 1
        });

        return computed;
    }

    private async Task<HashSet<(int FeedVersionId, string ServiceId)>> LoadValidServiceIdsAsync(
        DateOnly date, DayOfWeek dayOfWeek, CancellationToken ct)
    {
        var activeFvIds = await _db.FeedVersions
            .Where(fv => fv.IsActive)
            .Select(fv => fv.Id)
            .ToListAsync(ct);

        var activeCalendars = await _db.Calendars
            .Where(c => activeFvIds.Contains(c.FeedVersionId))
            .Where(c => c.StartDate <= date && c.EndDate >= date)
            .Where(c => dayOfWeek == DayOfWeek.Monday ? c.Monday :
                dayOfWeek == DayOfWeek.Tuesday ? c.Tuesday :
                dayOfWeek == DayOfWeek.Wednesday ? c.Wednesday :
                dayOfWeek == DayOfWeek.Thursday ? c.Thursday :
                dayOfWeek == DayOfWeek.Friday ? c.Friday :
                dayOfWeek == DayOfWeek.Saturday ? c.Saturday :
                dayOfWeek == DayOfWeek.Sunday ? c.Sunday : false)
            .Select(c => new { c.FeedVersionId, c.ServiceId })
            .ToListAsync(ct);

        var addedExceptions = await _db.CalendarDates
            .Where(cd => activeFvIds.Contains(cd.FeedVersionId))
            .Where(cd => cd.Date == date && cd.ExceptionType == 1)
            .Select(cd => new { cd.FeedVersionId, cd.ServiceId })
            .ToListAsync(ct);

        var removedExceptions = await _db.CalendarDates
            .Where(cd => activeFvIds.Contains(cd.FeedVersionId))
            .Where(cd => cd.Date == date && cd.ExceptionType == 2)
            .Select(cd => new { cd.FeedVersionId, cd.ServiceId })
            .ToListAsync(ct);

        HashSet<(int, string)> valid = [];
        foreach (var c in activeCalendars)
            valid.Add((c.FeedVersionId, c.ServiceId));
        foreach (var cd in addedExceptions)
            valid.Add((cd.FeedVersionId, cd.ServiceId));
        foreach (var cd in removedExceptions)
            valid.Remove((cd.FeedVersionId, cd.ServiceId));
        return valid;
    }

    // Groups by CanonicalStationId, orders by first trip's StopSequence. Display-only —
    // not a clean representation of bidirectional routes.
    public async Task<List<StationResponse>> GetRouteStopsAsync(int canonicalRouteId, CancellationToken ct)
    {
        // Known limitation: for bidirectional routes, GroupBy picks the first stop by sequence
        // for each station ID, which may arbitrarily select one direction's stop. See #99.
        var firstStopPerStation = await _db.StopTimes
            .Where(st => st.Trip.CanonicalRouteId == canonicalRouteId)
            .Where(st => st.CanonicalStationId.HasValue)
            .GroupBy(st => st.CanonicalStationId)
            .Select(g => new { StationId = g.Key!.Value, MinSequence = g.Min(st => st.StopSequence) })
            .OrderBy(x => x.MinSequence)
            .ToListAsync(ct);

        var stationIds = firstStopPerStation.Select(x => x.StationId).ToList();
        var stations = await _db.CanonicalStations
            .Where(cs => stationIds.Contains(cs.Id))
            .ToListAsync(ct);

        var stationMap = stations.ToDictionary(s => s.Id);
        return firstStopPerStation
            .Select(x => stationMap.TryGetValue(x.StationId, out var s) ? StationMapper.ToResponse(s) : null)
            .Where(s => s is not null)
            .ToList()!;
    }

    public async Task<List<TripResponse>> GetRouteTripsAsync(int canonicalRouteId, DateOnly date, CancellationToken ct)
    {
        var dayOfWeek = date.DayOfWeek;

        var validServices = await GetValidServiceIdsAsync(date, dayOfWeek, ct);

        var trips = await _db.Trips
            .Where(t => t.CanonicalRouteId == canonicalRouteId)
            .Where(t => t.FeedVersion.IsActive)
            .Select(t => new
            {
                t.Id,
                t.TripId,
                t.TripHeadsign,
                t.TripShortName,
                t.DirectionId,
                t.FeedVersionId,
                t.ServiceId,
                RouteName = t.CanonicalRoute != null
                    ? (t.CanonicalRoute.ShortName != "" ? t.CanonicalRoute.ShortName : t.CanonicalRoute.LongName)
                    : "",
                RouteType = t.CanonicalRoute != null ? t.CanonicalRoute.RouteType.ToString() : null
            })
            .ToListAsync(ct);

        return trips
            .Where(t => validServices.Contains((t.FeedVersionId, t.ServiceId)))
            .Select(t => new TripResponse
            {
                Id = t.Id,
                TripId = t.TripId,
                Headsign = t.TripHeadsign,
                ShortName = t.TripShortName,
                DirectionId = t.DirectionId,
                RouteName = t.RouteName,
                RouteType = t.RouteType,
                ActiveToday = true
            })
            .ToList();
    }

}
