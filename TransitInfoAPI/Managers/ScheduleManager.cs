using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Managers;

public class ScheduleManager
{
    private readonly TransitDbContext _db;

    public ScheduleManager(TransitDbContext db)
    {
        _db = db;
    }

    public async Task<List<DepartureDto>> GetDeparturesAsync(
        int canonicalStationId, DateTime from, int count, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(from);
        var fromTime = (int)from.TimeOfDay.TotalSeconds;

        var activeServiceIds = await GetActiveServiceIdsForDateAsync(today, ct);

        var departures = await _db.StopTimes
            .Where(st => st.CanonicalStationId == canonicalStationId)
            .Where(st => st.Trip.FeedVersion.IsActive)
            .Where(st => st.Trip != null && activeServiceIds.Contains(st.Trip.ServiceId))
            .Where(st => st.DepartureTime >= fromTime)
            .OrderBy(st => st.DepartureTime)
            .Take(count)
            .Select(st => new DepartureDto
            {
                TripId = st.Trip.TripId,
                RouteName = st.Trip.CanonicalRoute != null
                    ? (st.Trip.CanonicalRoute.ShortName ?? st.Trip.CanonicalRoute.LongName)
                    : st.Trip.TripShortName ?? "",
                Headsign = st.StopHeadsign ?? st.Trip.TripHeadsign ?? "",
                ScheduledDeparture = from.Date.AddSeconds(st.DepartureTime),
                EstimatedDeparture = null,
                DelaySeconds = null
            })
            .ToListAsync(ct);

        return departures;
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
        var activeServiceIds = await GetActiveServiceIdsForDateAsync(date, ct);

        var trips = await _db.Trips
            .Where(t => t.CanonicalRouteId == canonicalRouteId)
            .Where(t => t.FeedVersion.IsActive)
            .Where(t => activeServiceIds.Contains(t.ServiceId))
            .Select(t => new TripDto
            {
                Id = t.Id,
                TripId = t.TripId,
                Headsign = t.TripHeadsign,
                ShortName = t.TripShortName,
                DirectionId = t.DirectionId,
                RouteName = t.CanonicalRoute != null
                    ? (t.CanonicalRoute.ShortName ?? t.CanonicalRoute.LongName)
                    : "",
                RouteType = t.CanonicalRoute != null ? t.CanonicalRoute.RouteType.ToString() : null,
                ActiveToday = activeServiceIds.Contains(t.ServiceId)
            })
            .ToListAsync(ct);

        return trips;
    }

    private async Task<HashSet<string>> GetActiveServiceIdsForDateAsync(DateOnly date, CancellationToken ct)
    {
        var dayOfWeek = date.DayOfWeek;

        var calendarServices = await _db.Calendars
            .Where(c => c.StartDate <= date && c.EndDate >= date)
            .Where(c => dayOfWeek == DayOfWeek.Monday ? c.Monday :
                        dayOfWeek == DayOfWeek.Tuesday ? c.Tuesday :
                        dayOfWeek == DayOfWeek.Wednesday ? c.Wednesday :
                        dayOfWeek == DayOfWeek.Thursday ? c.Thursday :
                        dayOfWeek == DayOfWeek.Friday ? c.Friday :
                        dayOfWeek == DayOfWeek.Saturday ? c.Saturday : c.Sunday)
            .Select(c => c.ServiceId)
            .ToListAsync(ct);

        var addedExceptions = await _db.CalendarDates
            .Where(cd => cd.Date == date && cd.ExceptionType == 1)
            .Select(cd => cd.ServiceId)
            .ToListAsync(ct);

        var removedExceptions = await _db.CalendarDates
            .Where(cd => cd.Date == date && cd.ExceptionType == 2)
            .Select(cd => cd.ServiceId)
            .ToHashSetAsync(ct);

        var result = new HashSet<string>(calendarServices);
        result.UnionWith(addedExceptions);
        result.ExceptWith(removedExceptions);
        return result;
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
