using System.IO.Compression;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Routing;
using TransitInfoAPI.Routing.Export;

namespace GetThere.Tests.Routing;

/// <summary>
/// End-to-end export over an in-memory canonical model. Exercises the properties the plan's export
/// verification calls out: raw ids namespaced per version (so a shared id string yields two stops),
/// canonical stations emitted as parents, the three-arm stop-time resolution, drops surfaced rather
/// than emitted dangling, and a stops-only version contributing stops without failing the run.
/// </summary>
public class GtfsBundleExporterTests
{
    private static TransitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TransitDbContext>()
            .UseInMemoryDatabase($"export-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static GtfsBundleExporter Exporter(TransitDbContext db)
    {
        var options = Options.Create(new RoutingOptions
        {
            Timezone = "Europe/Zagreb",
            Scope = new BoundingBox { MinLat = 45.6, MinLon = 15.7, MaxLat = 45.95, MaxLon = 16.25 },
        });
        return new GtfsBundleExporter(db, options, NullLogger<GtfsBundleExporter>.Instance);
    }

    private static async Task SeedAsync(TransitDbContext db)
    {
        // Two active feed versions of two active feeds.
        db.Feeds.AddRange(
            new Feed { Id = 11, IsActive = true, OperatorId = 101, OnestopId = "f-zet", FeedId = "zet" },
            new Feed { Id = 12, IsActive = true, OperatorId = 101, OnestopId = "f-other", FeedId = "other" });
        db.FeedVersions.AddRange(
            new FeedVersion { Id = 1, FeedId = 11, IsActive = true },
            new FeedVersion { Id = 2, FeedId = 12, IsActive = true });

        db.Operators.Add(new Operator { Id = 101, GlobalId = "o-ZET", OnestopId = "o-zet", Name = "ZET" });

        // One canonical station acting as the parent for both raw stops.
        db.CanonicalStations.Add(new CanonicalStation
        {
            Id = 201, OnestopId = "HR-ZG-central", Name = "Central", IsActive = true,
            Latitude = 45.81, Longitude = 15.98, CountryId = 1,
        });

        // The same operator stop id "S1" imported under both versions — the collision case.
        db.RawStops.AddRange(
            new RawStop { Id = 501, FeedVersionId = 1, RawStopId = "S1", Name = "Central A", Lat = 45.81, Lon = 15.98, IsActive = true, CanonicalStationId = 201 },
            new RawStop { Id = 502, FeedVersionId = 2, RawStopId = "S1", Name = "Central B", Lat = 45.811, Lon = 15.981, IsActive = true, CanonicalStationId = 201 });

        db.CanonicalRoutes.Add(new CanonicalRoute
        {
            Id = 401, OnestopId = "r-ZET-1", ShortName = "1", LongName = "Line 1",
            RouteType = RouteType.Tram, IsActive = true, OperatorId = 101,
        });

        // Trip A (version 1) is routable; trip B (version 2) has no canonical route → skipped.
        db.Trips.AddRange(
            new Trip { Id = 301, FeedVersionId = 1, TripId = "T1", ServiceId = "svc1", CanonicalRouteId = 401 },
            new Trip { Id = 302, FeedVersionId = 2, TripId = "T2", ServiceId = "svc1", CanonicalRouteId = null });

        db.Calendars.Add(new Calendar
        {
            Id = 601, FeedVersionId = 1, ServiceId = "svc1",
            Monday = true, Tuesday = true, Wednesday = true, Thursday = true, Friday = true,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        });

        // Trip A's stop times exercise all three resolution arms.
        db.StopTimes.AddRange(
            new StopTime { Id = 701, TripId = 301, RawStopId = "S1", RawStopEntityId = 501, ArrivalTime = 3600, DepartureTime = 3600, StopSequence = 1 },  // arm 1
            new StopTime { Id = 702, TripId = 301, RawStopId = "S9", CanonicalStationId = 201, ArrivalTime = 3660, DepartureTime = 3660, StopSequence = 2 }, // arm 2
            new StopTime { Id = 703, TripId = 301, RawStopId = "S?", ArrivalTime = 3720, DepartureTime = 3720, StopSequence = 3 });                          // arm 3 → drop

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Exports_stops_routes_and_trips_with_reconciliation_and_resolution()
    {
        using var db = NewContext();
        await SeedAsync(db);

        var result = await Exporter(db).ExportAsync();
        var files = ReadZip(result.GtfsZip);

        var stops = files["stops.txt"];
        var stopTimes = files["stop_times.txt"];

        // Namespacing: the shared id "S1" produces two distinct exported stops, one per version.
        Assert.Contains("1:S1", stops);
        Assert.Contains("2:S1", stops);

        // Reconciliation reaches the bundle: the canonical station is a location_type=1 parent, and a
        // raw stop points at it via parent_station (its OnestopId, not an internal id).
        Assert.Contains("HR-ZG-central,Central,", stops);
        Assert.Matches(@"1:S1,Central A,[^\n]*,0,HR-ZG-central", stops);

        // Arm 1 (raw key) and arm 2 (canonical station) both reach stop_times; arm 3 is dropped.
        Assert.Contains("1:T1", stopTimes);           // trip present
        Assert.Contains(",1:S1,", $",{stopTimes},");   // arm 1 stop id present as a field
        Assert.Contains("HR-ZG-central", stopTimes);   // arm 2 resolved to the canonical station

        // The drop is reported, not silently emitted.
        Assert.True(result.Resolution.AnyDropped);
        Assert.Equal(1, result.Resolution.TotalDropped);
        Assert.Equal(1, result.Resolution.ByFeedVersion[1].ResolvedViaRawStop);
        Assert.Equal(1, result.Resolution.ByFeedVersion[1].ResolvedViaCanonicalStation);
        Assert.Equal(1, result.Resolution.ByFeedVersion[1].Dropped);

        // Trip B had no canonical route → skipped, and version 2 still contributed its stop.
        Assert.Equal(1, result.TripsSkippedNoRoute);
        Assert.DoesNotContain("2:T2", files["trips.txt"]);
    }

    [Fact]
    public async Task Deactivating_a_feed_drops_exactly_its_trips()
    {
        using var db = NewContext();
        await SeedAsync(db);

        // Deactivate feed 11 (the one carrying the routable trip T1).
        var feed = await db.Feeds.FirstAsync(f => f.Id == 11);
        feed.IsActive = false;
        await db.SaveChangesAsync();

        var result = await Exporter(db).ExportAsync();
        var files = ReadZip(result.GtfsZip);

        Assert.DoesNotContain("1:T1", files["trips.txt"]);   // its trips are gone
        Assert.DoesNotContain("1:S1", files["stops.txt"]);   // and its stops
        Assert.Contains("2:S1", files["stops.txt"]);         // the other feed's stop remains
    }

    private static Dictionary<string, string> ReadZip(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var files = new Dictionary<string, string>();
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            files[entry.FullName] = reader.ReadToEnd();
        }
        return files;
    }
}
