using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Models;

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

    public async Task<List<DepartureDto>> GetDeparturesAsync(
        int canonicalStationId, DateTime from, int count, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(from);
        var fromTime = (int)from.TimeOfDay.TotalSeconds;
        var dayOfWeek = today.DayOfWeek;

        var validServices = await GetValidServiceIdsAsync(today, dayOfWeek, ct);

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
                    ? (st.Trip.CanonicalRoute.ShortName != "" ? st.Trip.CanonicalRoute.ShortName : st.Trip.CanonicalRoute.LongName)
                    : (st.Trip.TripShortName ?? ""),
                Headsign = st.StopHeadsign ?? st.Trip.TripHeadsign ?? ""
            })
            .ToListAsync(ct);

        return rawDepartures
            .Where(d => validServices.Contains((d.FeedVersionId, d.ServiceId)))
            .OrderBy(d => d.DepartureTime)
            .Take(count)
            .Select(d =>
            {
                var (delay, estimated) = _realtime.GetStopDelay(d.TripId, d.StopSequence, from.Date.AddSeconds(d.DepartureTime));
                return new DepartureDto
                {
                    TripId = d.TripId,
                    RouteName = d.RouteName,
                    Headsign = d.Headsign,
                    ScheduledDeparture = from.Date.AddSeconds(d.DepartureTime),
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

    public async Task<List<StationDto>> GetRouteStopsAsync(int canonicalRouteId, CancellationToken ct)
    {
        var stops = await _db.StopTimes
            .Where(st => st.Trip.CanonicalRouteId == canonicalRouteId)
            .Where(st => st.CanonicalStationId.HasValue)
            .GroupBy(st => st.CanonicalStationId)
            .Select(g => g.OrderBy(st => st.StopSequence).First())
            .OrderBy(st => st.StopSequence)
            .Select(st => new StationDto
            {
                Id = st.CanonicalStation!.Id,
                GlobalId = st.CanonicalStation.GlobalId,
                OnestopId = st.CanonicalStation.OnestopId,
                Name = st.CanonicalStation.Name,
                Latitude = st.CanonicalStation.Latitude,
                Longitude = st.CanonicalStation.Longitude,
                StationType = st.CanonicalStation.StationType.ToString(),
                PrimaryRouteType = st.CanonicalStation.PrimaryRouteType.ToString()
            })
            .ToListAsync(ct);

        return stops;
    }

    public async Task<List<TripDto>> GetRouteTripsAsync(int canonicalRouteId, DateOnly date, CancellationToken ct)
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
            .Select(t => new TripDto
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

    public async Task<bool> IsServiceActiveOnAsync(string serviceId, DateOnly date, int feedVersionId, CancellationToken ct)
    {
        var dayOfWeek = date.DayOfWeek;

        var calendar = await _db.Calendars
            .FirstOrDefaultAsync(c => c.FeedVersionId == feedVersionId && c.ServiceId == serviceId, ct);

        if (calendar is null) return false;
        if (date < calendar.StartDate || date > calendar.EndDate) return false;

        var active = dayOfWeek switch
        {
            DayOfWeek.Monday => calendar.Monday,
            DayOfWeek.Tuesday => calendar.Tuesday,
            DayOfWeek.Wednesday => calendar.Wednesday,
            DayOfWeek.Thursday => calendar.Thursday,
            DayOfWeek.Friday => calendar.Friday,
            DayOfWeek.Saturday => calendar.Saturday,
            DayOfWeek.Sunday => calendar.Sunday,
            _ => false
        };

        if (!active) return false;

        var exception = await _db.CalendarDates
            .FirstOrDefaultAsync(cd => cd.FeedVersionId == feedVersionId && cd.ServiceId == serviceId && cd.Date == date, ct);

        if (exception is not null)
            return exception.ExceptionType == 1;

        return active;
    }
}
