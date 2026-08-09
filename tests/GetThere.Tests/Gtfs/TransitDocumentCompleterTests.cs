using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Services;

namespace GetThere.Tests.Gtfs;

/// <summary>
/// The completer's whole value is that it fills gaps <em>without guessing</em>. These pin both
/// halves: that each rule produces the one defensible answer, and that a sparse feed is left sparse
/// rather than padded into looking like a timetable it isn't.
/// </summary>
public class TransitDocumentCompleterTests
{
    private static TransitDocumentCompleter Completer(string timezone = "Europe/Zagreb")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Schedule:Timezone"] = timezone })
            .Build();

        return new TransitDocumentCompleter(NullLogger<TransitDocumentCompleter>.Instance, config);
    }

    private static Operator Op() => new()
    {
        Id = 7,
        Name = "Autotrolej",
        OnestopId = "o-autotrolej",
        Website = "https://autotrolej.test"
    };

    private static TransitDocument Doc(
        List<RawStopRecord>? stops = null,
        List<RawRouteRecord>? routes = null,
        List<RawTripRecord>? trips = null,
        List<RawAgencyRecord>? agencies = null,
        List<RawCalendarRecord>? calendar = null,
        bool hasStopTimes = false) => new()
        {
            OperatorId = 7,
            Stops = stops,
            Routes = routes,
            Trips = trips,
            Agencies = agencies,
            Calendar = calendar,
            HasStopTimes = hasStopTimes
        };

    private static readonly List<RawStopRecord> OneStop =
        [new() { StopId = "S1", StopName = "Jelačić", StopLat = 45.81, StopLon = 15.98 }];

    // ── Agency synthesis ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_agency_is_built_from_the_operator()
    {
        var result = await Completer().CompleteAsync(Doc(stops: OneStop), Op(), null, null);

        var agency = Assert.Single(result.Agencies!);
        Assert.Equal("o-autotrolej", agency.AgencyId);
        Assert.Equal("Autotrolej", agency.AgencyName);
        Assert.Equal("https://autotrolej.test", agency.AgencyUrl);
        Assert.Equal("Europe/Zagreb", agency.AgencyTimezone);
        Assert.Contains("agencies", result.SynthesizedSections);
    }

    [Fact]
    public async Task Synthesized_agency_uses_the_configured_timezone()
    {
        var result = await Completer("Europe/Vienna").CompleteAsync(Doc(stops: OneStop), Op(), null, null);

        Assert.Equal("Europe/Vienna", result.Agencies![0].AgencyTimezone);
    }

    [Fact]
    public async Task An_agency_the_source_published_is_left_alone()
    {
        var published = new List<RawAgencyRecord>
        {
            new() { AgencyId = "real", AgencyName = "As Published", AgencyTimezone = "Europe/Berlin" }
        };

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, agencies: published), Op(), null, null);

        Assert.Equal("As Published", result.Agencies![0].AgencyName);
        Assert.DoesNotContain("agencies", result.SynthesizedSections);
    }

    // ── Calendar synthesis ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_calendar_becomes_an_always_on_service_across_the_window()
    {
        var trips = new List<RawTripRecord> { new() { TripId = "T1", RouteId = "R1" } };
        var window = (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, trips: trips), Op(), window, null);

        var calendar = Assert.Single(result.Calendar!);
        Assert.Equal("20260101", calendar.StartDate);
        Assert.Equal("20261231", calendar.EndDate);
        Assert.Equal(1, calendar.Monday);
        Assert.Equal(1, calendar.Sunday);
        Assert.Contains("calendar", result.SynthesizedSections);
    }

    [Fact]
    public async Task Trips_without_a_service_are_attached_to_the_synthesized_one()
    {
        var trips = new List<RawTripRecord> { new() { TripId = "T1", RouteId = "R1", ServiceId = "" } };
        var window = (new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, trips: trips), Op(), window, null);

        Assert.Equal(result.Calendar![0].ServiceId, result.Trips![0].ServiceId);
    }

    [Fact]
    public async Task A_trip_that_already_names_a_service_keeps_it()
    {
        var trips = new List<RawTripRecord> { new() { TripId = "T1", RouteId = "R1", ServiceId = "weekday" } };
        var window = (new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, trips: trips), Op(), window, null);

        Assert.Equal("weekday", result.Trips![0].ServiceId);
    }

    [Fact]
    public async Task No_service_window_means_no_calendar_is_invented()
    {
        var trips = new List<RawTripRecord> { new() { TripId = "T1", RouteId = "R1" } };

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, trips: trips), Op(), null, null);

        Assert.Null(result.Calendar);
        Assert.DoesNotContain("calendar", result.SynthesizedSections);
    }

    [Fact]
    public async Task A_calendar_the_source_published_is_left_alone()
    {
        var published = new List<RawCalendarRecord>
        {
            new() { ServiceId = "weekday", Monday = 1, Saturday = 0, Sunday = 0, StartDate = "20260101", EndDate = "20260601" }
        };
        var trips = new List<RawTripRecord> { new() { TripId = "T1", RouteId = "R1", ServiceId = "weekday" } };
        var window = (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, trips: trips, calendar: published), Op(), window, null);

        Assert.Equal("weekday", Assert.Single(result.Calendar!).ServiceId);
        Assert.Equal(0, result.Calendar![0].Sunday);
        Assert.DoesNotContain("calendar", result.SynthesizedSections);
    }

    // ── Trip derivation ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Trips_are_recovered_from_the_trip_ids_the_stop_times_already_carry()
    {
        var stopTimes = Stream(
        [
            new() { TripId = "T1", StopId = "S1", DepartureTime = "08:00:00", StopSequence = 1 },
            new() { TripId = "T1", StopId = "S2", DepartureTime = "08:05:00", StopSequence = 2 },
            new() { TripId = "T2", StopId = "S1", DepartureTime = "09:00:00", StopSequence = 1 }
        ]);

        var result = await Completer().CompleteAsync(
            Doc(stops: OneStop, hasStopTimes: true), Op(), null, stopTimes);

        Assert.Equal(2, result.Trips!.Count);
        Assert.Contains(result.Trips, t => t.TripId == "T1");
        Assert.Contains(result.Trips, t => t.TripId == "T2");
        Assert.Contains("trips", result.SynthesizedSections);
    }

    [Fact]
    public async Task Trips_the_source_published_are_not_replaced_by_derived_ones()
    {
        var published = new List<RawTripRecord> { new() { TripId = "REAL", RouteId = "R1", TripHeadsign = "Kept" } };
        var stopTimes = Stream([new() { TripId = "OTHER", StopId = "S1", DepartureTime = "08:00:00" }]);

        var result = await Completer().CompleteAsync(
            Doc(stops: OneStop, trips: published, hasStopTimes: true), Op(), null, stopTimes);

        Assert.Equal("REAL", Assert.Single(result.Trips!).TripId);
        Assert.DoesNotContain("trips", result.SynthesizedSections);
    }

    // ── Restraint ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_stops_only_document_stays_stops_only()
    {
        var result = await Completer().CompleteAsync(Doc(stops: OneStop), Op(), null, null);

        Assert.Equal(FeedCompleteness.StopsOnly, result.Completeness);
        Assert.Null(result.Routes);
        Assert.Null(result.Trips);
        Assert.Null(result.Calendar);
    }

    [Fact]
    public async Task A_stops_and_routes_document_stays_network_level()
    {
        var routes = new List<RawRouteRecord> { new() { RouteId = "R1", RouteShortName = "1", RouteType = 3 } };

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, routes: routes), Op(), null, null);

        Assert.Equal(FeedCompleteness.Network, result.Completeness);
        Assert.Null(result.Trips);
    }

    [Fact]
    public async Task A_document_with_trips_and_stop_times_is_a_full_schedule()
    {
        var routes = new List<RawRouteRecord> { new() { RouteId = "R1", RouteShortName = "1", RouteType = 3 } };
        var trips = new List<RawTripRecord> { new() { TripId = "T1", RouteId = "R1" } };

        var result = await Completer().CompleteAsync(
            Doc(stops: OneStop, routes: routes, trips: trips, hasStopTimes: true), Op(), null, null);

        Assert.Equal(FeedCompleteness.Schedule, result.Completeness);
    }

    [Fact]
    public async Task The_completer_never_invents_stops_or_routes()
    {
        var window = (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = await Completer().CompleteAsync(Doc(), Op(), window, null);

        Assert.Null(result.Stops);
        Assert.Null(result.Routes);
    }

    // ── Default route type ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_single_mode_feed_attributes_that_mode_to_its_stops()
    {
        // Without this every stop in a timetable-less feed reconciles as Inactive, because route
        // types are normally derived from stop times and there are none to derive from.
        var routes = new List<RawRouteRecord>
        {
            new() { RouteId = "1", RouteType = 0 },
            new() { RouteId = "6", RouteType = 0 }
        };

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, routes: routes), Op(), null, null);

        Assert.Equal(RouteType.Tram, result.DefaultRouteType);
    }

    [Fact]
    public async Task A_mixed_mode_feed_abstains_rather_than_picking_one()
    {
        var routes = new List<RawRouteRecord>
        {
            new() { RouteId = "1", RouteType = 0 },   // tram
            new() { RouteId = "109", RouteType = 3 }  // bus
        };

        var result = await Completer().CompleteAsync(Doc(stops: OneStop, routes: routes), Op(), null, null);

        Assert.Null(result.DefaultRouteType);
    }

    [Fact]
    public async Task A_feed_with_no_routes_has_no_default_mode()
    {
        var result = await Completer().CompleteAsync(Doc(stops: OneStop), Op(), null, null);

        Assert.Null(result.DefaultRouteType);
    }

    private static Func<IAsyncEnumerable<List<RawStopTimeRecord>>> Stream(List<RawStopTimeRecord> rows)
        => () => OneBatch(rows);

    private static async IAsyncEnumerable<List<RawStopTimeRecord>> OneBatch(List<RawStopTimeRecord> rows)
    {
        yield return rows;
        await Task.CompletedTask;
    }
}
