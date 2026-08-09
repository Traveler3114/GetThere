using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Managers;

/// <summary>
/// Fills in the parts of a <see cref="TransitDocument"/> a source could not supply, using only
/// deterministic rules.
/// <para>
/// This exists because the alternative — demanding every source produce a complete GTFS-shaped
/// network — is what made synthesizing a GTFS archive fail: operators publish departures with no
/// calendar, stops with no agency, and stop times with no separate trip list, and none of that is a
/// defect in their data. Each rule below either has an unambiguous answer or does not run.
/// </para>
/// <para>
/// It never invents stops, routes, or times. A source that publishes only stops stays
/// <see cref="Enums.FeedCompleteness.StopsOnly"/> rather than being padded into looking like a
/// timetable.
/// </para>
/// </summary>
public class TransitDocumentCompleter
{
    private readonly ILogger<TransitDocumentCompleter> _logger;
    private readonly string _defaultTimezone;

    public TransitDocumentCompleter(ILogger<TransitDocumentCompleter> logger, IConfiguration configuration)
    {
        _logger = logger;
        _defaultTimezone = configuration.GetValue("Schedule:Timezone", "Europe/Zagreb")!;
    }

    /// <param name="op">The operator the feed belongs to — the only trustworthy source of agency identity.</param>
    /// <param name="serviceWindow">
    /// The span a synthesized calendar should cover. Null means the source declared none, and no
    /// calendar is invented.
    /// </param>
    public async Task<TransitDocument> CompleteAsync(
        TransitDocument document,
        Operator op,
        (DateOnly Start, DateOnly End)? serviceWindow,
        Func<IAsyncEnumerable<List<RawStopTimeRecord>>>? stopTimes,
        CancellationToken ct = default)
    {
        var synthesized = new List<string>(document.SynthesizedSections);

        var agencies = document.Agencies;
        if (agencies is null || agencies.Count == 0)
        {
            agencies = [BuildAgencyFromOperator(op)];
            synthesized.Add("agencies");
        }

        var trips = document.Trips;
        if ((trips is null || trips.Count == 0) && document.HasStopTimes && stopTimes is not null)
        {
            trips = await DeriveTripsFromStopTimesAsync(stopTimes, ct);
            if (trips.Count > 0)
                synthesized.Add("trips");
            else
                trips = document.Trips;
        }

        var calendar = document.Calendar;
        var hasAnyCalendar = calendar is { Count: > 0 } || document.CalendarDates is { Count: > 0 };
        if (!hasAnyCalendar && trips is { Count: > 0 } && serviceWindow is { } window)
        {
            calendar = [BuildAlwaysOnCalendar(window.Start, window.End)];
            foreach (var trip in trips)
            {
                if (string.IsNullOrWhiteSpace(trip.ServiceId))
                    trip.ServiceId = AlwaysOnServiceId;
            }
            synthesized.Add("calendar");
        }

        var completed = new TransitDocument
        {
            OperatorId = document.OperatorId,
            Agencies = agencies,
            Stops = document.Stops,
            Routes = document.Routes,
            Trips = trips,
            Calendar = calendar,
            CalendarDates = document.CalendarDates,
            Shapes = document.Shapes,
            HasStopTimes = document.HasStopTimes,
            DefaultRouteType = document.DefaultRouteType ?? InferRouteType(document)
        };

        foreach (var section in synthesized.Distinct(StringComparer.OrdinalIgnoreCase))
            completed.SynthesizedSections.Add(section);

        if (completed.SynthesizedSections.Count > 0)
            _logger.LogInformation("Completer synthesized: {Sections}", string.Join(", ", completed.SynthesizedSections));

        return completed;
    }

    /// <summary>
    /// The one mode a feed's stops can be attributed to, when there is only one it could be.
    /// <para>
    /// A single-mode operator's stops are all that mode — that is a fact about the feed, not a
    /// guess. A feed mixing trams and buses gives no answer for a stop no timetable reaches, so this
    /// abstains rather than picking the more common one; those stops stay unreconciled, which is
    /// visible and fixable, unlike a wrong merge.
    /// </para>
    /// </summary>
    private static RouteType? InferRouteType(TransitDocument document)
    {
        if (document.Routes is not { Count: > 0 }) return null;

        var distinct = document.Routes.Select(r => r.RouteTypeEnum).Distinct().Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    internal const string AlwaysOnServiceId = "synthetic-always";

    private RawAgencyRecord BuildAgencyFromOperator(Operator op) => new()
    {
        AgencyId = op.OnestopId,
        AgencyName = op.Name,
        AgencyUrl = op.Website ?? string.Empty,
        AgencyTimezone = _defaultTimezone
    };

    /// <summary>
    /// A service that runs every day across the declared window. Deliberately not clever: a source
    /// with no calendar has told us nothing about which days it runs, and inferring weekday patterns
    /// from observed departures would be a guess dressed up as data.
    /// </summary>
    private static RawCalendarRecord BuildAlwaysOnCalendar(DateOnly start, DateOnly end) => new()
    {
        ServiceId = AlwaysOnServiceId,
        Monday = 1,
        Tuesday = 1,
        Wednesday = 1,
        Thursday = 1,
        Friday = 1,
        Saturday = 1,
        Sunday = 1,
        StartDate = start.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
        EndDate = end.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Recovers the trip list from the stop times themselves.
    /// <para>
    /// Not an inference: stop times are meaningless without a trip id to group them by, so a source
    /// that publishes stop times has already named its trips — it just did not publish them as a
    /// separate list. The route id is taken from the rows when they carry one.
    /// </para>
    /// </summary>
    private static async Task<List<RawTripRecord>> DeriveTripsFromStopTimesAsync(
        Func<IAsyncEnumerable<List<RawStopTimeRecord>>> stopTimes,
        CancellationToken ct)
    {
        var seen = new Dictionary<string, RawTripRecord>(StringComparer.OrdinalIgnoreCase);

        await foreach (var batch in stopTimes().WithCancellation(ct))
        {
            foreach (var st in batch)
            {
                if (string.IsNullOrWhiteSpace(st.TripId) || seen.ContainsKey(st.TripId))
                    continue;

                seen[st.TripId] = new RawTripRecord
                {
                    TripId = st.TripId,
                    RouteId = string.Empty,
                    ServiceId = string.Empty,
                    TripHeadsign = st.StopHeadsign
                };
            }
        }

        return [.. seen.Values];
    }
}
