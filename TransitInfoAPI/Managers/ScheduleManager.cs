using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Managers;

public class ScheduleManager
{
    private readonly TransitDbContext _db;
    private readonly RealtimeManager _realtime;

    public ScheduleManager(TransitDbContext db, RealtimeManager realtime)
    {
        _db = db;
        _realtime = realtime;
    }

    public async Task<List<DepartureResponse>> GetDeparturesAsync(
        int canonicalStationId, DateTime from, int count, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(from);
        var fromTime = (int)Math.Round(from.TimeOfDay.TotalSeconds);

        var validServices = await GetValidServiceIdsAsync(today, today.DayOfWeek, ct);
        var tomorrow = today.AddDays(1);
        var validServicesTomorrow = await GetValidServiceIdsAsync(tomorrow, tomorrow.DayOfWeek, ct);

        var rawDepartures = await _db.StopTimes
            .Where(st => st.CanonicalStationId == canonicalStationId)
            .Where(st => st.Trip.FeedVersion.IsActive)
            .Where(st => st.DepartureTime >= fromTime)
            .Select(st => new
            {
                st.Trip.TripId,
                st.StopSequence,
                st.Trip.FeedVersionId,
                st.Trip.ServiceId,
                st.DepartureTime,
                RouteName = st.Trip.CanonicalRoute != null
                    ? (st.Trip.CanonicalRoute.ShortName != "" && st.Trip.CanonicalRoute.LongName != ""
                        ? st.Trip.CanonicalRoute.ShortName + " - " + st.Trip.CanonicalRoute.LongName
                        : st.Trip.CanonicalRoute.ShortName != ""
                            ? st.Trip.CanonicalRoute.ShortName
                            : st.Trip.CanonicalRoute.LongName)
                    : (st.Trip.TripShortName ?? ""),
                Headsign = ""
            })
            .ToListAsync(ct);

        return rawDepartures
            .Where(d => d.DepartureTime >= 86400
                ? validServicesTomorrow.Contains((d.FeedVersionId, d.ServiceId))
                : validServices.Contains((d.FeedVersionId, d.ServiceId)))
            .OrderBy(d => d.DepartureTime)
            .Take(count)
            .Select(d =>
            {
                var departureTime = d.DepartureTime >= 86400
                    ? from.Date.AddDays(1).AddSeconds(d.DepartureTime - 86400)
                    : from.Date.AddSeconds(d.DepartureTime);
                var (delay, estimated) = _realtime.GetStopDelay(d.TripId, d.StopSequence, departureTime);
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

    private async Task<HashSet<(int FeedVersionId, string ServiceId)>> GetValidServiceIdsAsync(
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

        var valid = new HashSet<(int, string)>();
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
                t.Id, t.TripId, t.TripHeadsign, t.TripShortName, t.DirectionId,
                t.FeedVersionId, t.ServiceId,
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
