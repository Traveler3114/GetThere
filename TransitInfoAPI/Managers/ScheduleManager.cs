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

        var rawDepartures = await _db.StopTimes
            .Where(st => st.CanonicalStationId == canonicalStationId)
            .Where(st => st.Trip.FeedVersion.IsActive)
            .Where(st => st.DepartureTime >= fromTime)
            .Where(st =>
                (_db.Calendars.Any(c =>
                    c.FeedVersion.IsActive &&
                    c.FeedVersionId == st.Trip.FeedVersionId &&
                    c.ServiceId == st.Trip.ServiceId &&
                    c.StartDate <= today && c.EndDate >= today &&
                    ((dayOfWeek == DayOfWeek.Monday && c.Monday) ||
                     (dayOfWeek == DayOfWeek.Tuesday && c.Tuesday) ||
                     (dayOfWeek == DayOfWeek.Wednesday && c.Wednesday) ||
                     (dayOfWeek == DayOfWeek.Thursday && c.Thursday) ||
                     (dayOfWeek == DayOfWeek.Friday && c.Friday) ||
                     (dayOfWeek == DayOfWeek.Saturday && c.Saturday) ||
                     (dayOfWeek == DayOfWeek.Sunday && c.Sunday))) &&
                !_db.CalendarDates.Any(cd =>
                    cd.FeedVersion.IsActive &&
                    cd.FeedVersionId == st.Trip.FeedVersionId &&
                    cd.ServiceId == st.Trip.ServiceId &&
                    cd.Date == today && cd.ExceptionType == 2)) ||
                _db.CalendarDates.Any(cd =>
                    cd.FeedVersion.IsActive &&
                    cd.FeedVersionId == st.Trip.FeedVersionId &&
                    cd.ServiceId == st.Trip.ServiceId &&
                    cd.Date == today && cd.ExceptionType == 1))
            .OrderBy(st => st.DepartureTime)
            .Take(count)
            .Select(st => new
            {
                st.Trip.TripId,
                st.StopSequence,
                RouteName = st.Trip.CanonicalRoute != null
                    ? (st.Trip.CanonicalRoute.ShortName != "" ? st.Trip.CanonicalRoute.ShortName : st.Trip.CanonicalRoute.LongName)
                    : (st.Trip.TripShortName ?? ""),
                Headsign = st.StopHeadsign ?? st.Trip.TripHeadsign ?? "",
                ScheduledDeparture = from.Date.AddSeconds(st.DepartureTime)
            })
            .ToListAsync(ct);

        return rawDepartures.Select(d =>
        {
            var (delay, estimated) = _realtime.GetStopDelay(d.TripId, d.StopSequence, d.ScheduledDeparture);
            return new DepartureDto
            {
                TripId = d.TripId,
                RouteName = d.RouteName,
                Headsign = d.Headsign,
                ScheduledDeparture = d.ScheduledDeparture,
                EstimatedDeparture = estimated,
                DelaySeconds = delay
            };
        }).ToList();
    }

    public async Task<List<StationDto>> GetRouteStopsAsync(int canonicalRouteId, CancellationToken ct)
    {
        var stops = await _db.StopTimes
            .Where(st => st.Trip.CanonicalRouteId == canonicalRouteId)
            .Where(st => st.CanonicalStationId.HasValue)
            .GroupBy(st => st.CanonicalStationId)
            .Select(g => g.First())
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

        var trips = await _db.Trips
            .Where(t => t.CanonicalRouteId == canonicalRouteId)
            .Where(t => t.FeedVersion.IsActive)
            .Where(t =>
                (_db.Calendars.Any(c =>
                    c.FeedVersion.IsActive &&
                    c.FeedVersionId == t.FeedVersionId &&
                    c.ServiceId == t.ServiceId &&
                    c.StartDate <= date && c.EndDate >= date &&
                    ((dayOfWeek == DayOfWeek.Monday && c.Monday) ||
                     (dayOfWeek == DayOfWeek.Tuesday && c.Tuesday) ||
                     (dayOfWeek == DayOfWeek.Wednesday && c.Wednesday) ||
                     (dayOfWeek == DayOfWeek.Thursday && c.Thursday) ||
                     (dayOfWeek == DayOfWeek.Friday && c.Friday) ||
                     (dayOfWeek == DayOfWeek.Saturday && c.Saturday) ||
                     (dayOfWeek == DayOfWeek.Sunday && c.Sunday))) &&
                !_db.CalendarDates.Any(cd =>
                    cd.FeedVersion.IsActive &&
                    cd.FeedVersionId == t.FeedVersionId &&
                    cd.ServiceId == t.ServiceId &&
                    cd.Date == date && cd.ExceptionType == 2)) ||
                _db.CalendarDates.Any(cd =>
                    cd.FeedVersion.IsActive &&
                    cd.FeedVersionId == t.FeedVersionId &&
                    cd.ServiceId == t.ServiceId &&
                    cd.Date == date && cd.ExceptionType == 1))
            .Select(t => new TripDto
            {
                Id = t.Id,
                TripId = t.TripId,
                Headsign = t.TripHeadsign,
                ShortName = t.TripShortName,
                DirectionId = t.DirectionId,
                RouteName = t.CanonicalRoute != null
                    ? (t.CanonicalRoute.ShortName != "" ? t.CanonicalRoute.ShortName : t.CanonicalRoute.LongName)
                    : "",
                RouteType = t.CanonicalRoute != null ? t.CanonicalRoute.RouteType.ToString() : null,
                ActiveToday = true
            })
            .ToListAsync(ct);

        return trips;
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
