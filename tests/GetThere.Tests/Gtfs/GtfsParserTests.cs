using Microsoft.Extensions.Logging.Abstractions;

using NetTopologySuite.Geometries;

using TransitInfoAPI.Services;

namespace GetThere.Tests.Gtfs;

/// <summary>
/// Characterization tests for the GTFS parser — the front of the import pipeline, and until now
/// entirely untested despite being the code that decides what enters the transit database.
/// <para>
/// These pin down current behaviour, including the parts that are judgement calls rather than
/// obvious correctness, so that the pipeline can be refactored against something.
/// </para>
/// </summary>
public class GtfsParserTests
{
    private static GtfsParser CreateParser() => new(NullLogger<GtfsParser>.Instance);

    [Fact]
    public void A_minimal_feed_validates()
    {
        using var archive = GtfsFeedBuilder.Minimal().Build();

        var result = CreateParser().ValidateGtfs(archive);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.True(result.HasAgency);
        Assert.True(result.HasStops);
        Assert.True(result.HasRoutes);
        Assert.True(result.HasTrips);
        Assert.True(result.HasStopTimes);
    }

    [Theory]
    [InlineData("agency.txt")]
    [InlineData("stops.txt")]
    [InlineData("routes.txt")]
    [InlineData("trips.txt")]
    [InlineData("stop_times.txt")]
    public void A_feed_missing_a_required_file_is_rejected(string required)
    {
        using var archive = GtfsFeedBuilder.Minimal().Without(required).Build();

        var result = CreateParser().ValidateGtfs(archive);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Stops_are_read_with_their_coordinates()
    {
        using var archive = GtfsFeedBuilder.Minimal().Build();

        var (stops, dropped) = CreateParser().ParseStops(archive);

        Assert.Equal(0, dropped);
        Assert.Equal(2, stops.Count);

        var first = stops.Single(s => s.StopId == "S1");
        Assert.Equal("Britanski trg", first.StopName);
        Assert.Equal(45.8131, first.StopLat, 4);
        Assert.Equal(15.9619, first.StopLon, 4);
    }

    [Theory]
    // Out of range in each direction.
    [InlineData("91.0", "15.0")]
    [InlineData("-91.0", "15.0")]
    [InlineData("45.0", "181.0")]
    [InlineData("45.0", "-181.0")]
    // Null Island: legal coordinates, but in practice a missing value rather than a real stop in
    // the Gulf of Guinea. Dropping it is a deliberate choice, so it is pinned here.
    [InlineData("0.0", "0.0")]
    public void A_stop_with_unusable_coordinates_is_dropped_and_counted(string lat, string lon)
    {
        using var archive = GtfsFeedBuilder.Minimal()
            .With("stops.txt",
                "stop_id,stop_name,stop_lat,stop_lon,location_type\n" +
                "S1,Britanski trg,45.8131,15.9619,0\n" +
                $"BAD,Broken,{lat},{lon},0\n")
            .Build();

        var (stops, dropped) = CreateParser().ParseStops(archive);

        Assert.Equal(1, dropped);
        Assert.Equal("S1", Assert.Single(stops).StopId);
    }

    [Fact]
    public void Shape_points_are_ordered_by_sequence_not_by_file_order()
    {
        // Sequence numbers deliberately out of order in the file, and not contiguous — a shape whose
        // points are joined in file order draws a line that zigzags back on itself.
        using var archive = GtfsFeedBuilder.Minimal()
            .With("shapes.txt",
                "shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence\n" +
                "SH1,45.0,15.0,30\n" +
                "SH1,45.2,15.2,10\n" +
                "SH1,45.1,15.1,20\n")
            .Build();

        var shapes = CreateParser().ParseShapes(archive);

        var line = Assert.Contains("SH1", shapes);
        Assert.Equal(3, line.Coordinates.Length);
        Assert.Equal(45.2, line.Coordinates[0].Y, 4);
        Assert.Equal(45.1, line.Coordinates[1].Y, 4);
        Assert.Equal(45.0, line.Coordinates[2].Y, 4);
    }

    [Fact]
    public void Shape_ids_are_matched_without_regard_to_case()
    {
        using var archive = GtfsFeedBuilder.Minimal()
            .With("shapes.txt",
                "shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence\n" +
                "sh1,45.0,15.0,1\n" +
                "SH1,45.1,15.1,2\n")
            .Build();

        var shapes = CreateParser().ParseShapes(archive);

        // One shape, not two: the lookup is case-insensitive, so these are the same shape.
        Assert.Single(shapes);
    }

    [Fact]
    public void A_feed_without_shapes_yields_none_rather_than_failing()
    {
        using var archive = GtfsFeedBuilder.Minimal().Build();

        var shapes = CreateParser().ParseShapes(archive);

        Assert.Empty(shapes);
    }

    [Theory]
    [InlineData("08:00:00", 28800)]
    [InlineData("00:00:00", 0)]
    [InlineData("8:05:00", 29100)]      // seven characters, zero-padded on read
    [InlineData("25:30:00", 91800)]     // past midnight, which GTFS allows and must not wrap
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("not-a-time", null)]
    [InlineData("08:00", null)]
    public void Gtfs_times_are_seconds_since_midnight(string raw, int? expected)
    {
        Assert.Equal(expected, GtfsParser.ParseGtfsTimeToSeconds(raw));
    }

    [Fact]
    public void Stop_times_carry_their_sequence_and_stop()
    {
        using var archive = GtfsFeedBuilder.Minimal().Build();

        var stopTimes = CreateParser().ParseStopTimes(archive);

        Assert.Equal(2, stopTimes.Count);
        Assert.Equal("S1", stopTimes[0].StopId);
        Assert.Equal(1, stopTimes[0].StopSequence);
        Assert.Equal("S2", stopTimes[1].StopId);
        Assert.Equal(2, stopTimes[1].StopSequence);
    }

    [Fact]
    public async Task Batched_stop_times_return_the_same_rows_as_reading_them_whole()
    {
        var rows = string.Concat(Enumerable.Range(1, 25)
            .Select(i => $"T1,08:{i:00}:00,08:{i:00}:00,S1,{i}\n"));

        using var archive = GtfsFeedBuilder.Minimal()
            .With("stop_times.txt", "trip_id,arrival_time,departure_time,stop_id,stop_sequence\n" + rows)
            .Build();

        var parser = CreateParser();

        var batched = new List<TransitInfoAPI.Services.RawStopTimeRecord>();
        await foreach (var batch in parser.ParseStopTimesBatchedAsync(archive, batchSize: 10))
            batched.AddRange(batch);

        // Batching exists so a large feed does not have to be held in memory at once; it must not
        // change what is imported, including at a boundary that does not divide evenly.
        Assert.Equal(25, batched.Count);
        Assert.Equal(Enumerable.Range(1, 25), batched.Select(r => r.StopSequence));
    }

    [Fact]
    public void The_convex_hull_of_a_feed_encloses_its_stops()
    {
        using var archive = GtfsFeedBuilder.Minimal()
            .With("stops.txt",
                "stop_id,stop_name,stop_lat,stop_lon,location_type\n" +
                "S1,SW,45.0,15.0,0\n" +
                "S2,NW,46.0,15.0,0\n" +
                "S3,NE,46.0,16.0,0\n" +
                "S4,SE,45.0,16.0,0\n" +
                "S5,Middle,45.5,15.5,0\n")
            .Build();

        var parser = CreateParser();
        var (stops, _) = parser.ParseStops(archive);

        var hull = parser.ComputeConvexHull(stops);

        Assert.IsType<Polygon>(hull);
        Assert.True(hull.Contains(new Point(15.5, 45.5) { SRID = hull.SRID }));

        // SQL Server rejects a geography polygon wound clockwise, so the hull is normalised to
        // counter-clockwise before it is stored.
        Assert.True(NetTopologySuite.Algorithm.Orientation.IsCCW(((Polygon)hull).Shell.Coordinates));
    }

    [Fact]
    public void An_empty_stop_list_yields_an_empty_geometry_rather_than_throwing()
    {
        var hull = CreateParser().ComputeConvexHull([]);

        Assert.True(hull.IsEmpty);
    }

    [Fact]
    public void The_content_hash_is_stable_across_rebuilds_and_changes_with_content()
    {
        var parser = CreateParser();

        using var first = GtfsFeedBuilder.Minimal().Build();
        using var identical = GtfsFeedBuilder.Minimal().Build();
        using var altered = GtfsFeedBuilder.Minimal()
            .With("routes.txt",
                "route_id,agency_id,route_short_name,route_long_name,route_type\n" +
                "R1,ZET,7,Different route,0\n")
            .Build();

        var hash = parser.ComputeGtfsSha1(first);

        // The hash is what decides whether a fetched feed is a new version worth importing, so an
        // unchanged feed must hash the same and a changed one must not.
        Assert.Equal(hash, parser.ComputeGtfsSha1(identical));
        Assert.NotEqual(hash, parser.ComputeGtfsSha1(altered));
    }
}
