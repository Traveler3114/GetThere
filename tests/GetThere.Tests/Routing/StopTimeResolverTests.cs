using TransitInfoAPI.Routing.Export;

namespace GetThere.Tests.Routing;

/// <summary>
/// The export's resolution rule is the join between a source's timetable and the map's geometry. Its
/// arms — backfilled FK, the same-version (FeedVersionId, RawStopId) key, the canonical station, and
/// drop — decide whether a trip reaches routing, reaches it attached to the right place, or is
/// correctly withheld. A wrong arm mis-routes people while the graph still builds, so these are pinned.
/// </summary>
public class StopTimeResolverTests
{
    private static StopTimeResolver Resolver()
    {
        var rawStops = new Dictionary<int, RawStopRef>
        {
            [10] = new RawStopRef(FeedVersionId: 1, RawStopId: "S1"),
            [11] = new RawStopRef(FeedVersionId: 2, RawStopId: "S1"), // same string, different version
        };
        var canonical = new Dictionary<int, string>
        {
            [100] = "HR-ZG-central",
        };
        return new StopTimeResolver(rawStops, canonical);
    }

    [Fact]
    public void Arm_1_resolves_through_the_backfilled_foreign_key_when_set()
    {
        var r = Resolver().Resolve(rawStopEntityId: 10, canonicalStationId: 100, feedVersionId: 1, rawStopId: "S1");

        Assert.Equal(ExportedStopKind.RawStop, r.Kind);
        Assert.Equal(ExportedStopId.Encode(1, "S1"), r.StopId);
    }

    [Fact]
    public void Arm_2_resolves_through_the_same_version_key_when_the_backfill_FK_is_null()
    {
        // The reconciliation backfill has not populated RawStopEntityId/CanonicalStationId, but the
        // raw stop was still exported — its (version, id) key is its exported identity, so it resolves.
        var r = Resolver().Resolve(rawStopEntityId: null, canonicalStationId: null, feedVersionId: 2, rawStopId: "S1");

        Assert.Equal(ExportedStopKind.RawStop, r.Kind);
        Assert.Equal(ExportedStopId.Encode(2, "S1"), r.StopId);
    }

    [Fact]
    public void Arm_3_resolves_through_the_canonical_station_for_a_cross_feed_reference()
    {
        // A source publishing stop times against another feed's stop id: the id is not in this
        // version's raw stops (arm 2 misses), so it resolves to the canonical station instead.
        var r = Resolver().Resolve(rawStopEntityId: null, canonicalStationId: 100, feedVersionId: 9, rawStopId: "elsewhere");

        Assert.Equal(ExportedStopKind.CanonicalStation, r.Kind);
        Assert.Equal("HR-ZG-central", r.StopId);
    }

    [Fact]
    public void Drops_when_no_arm_resolves()
    {
        var r = Resolver().Resolve(rawStopEntityId: null, canonicalStationId: null, feedVersionId: 9, rawStopId: "unknown");

        Assert.Equal(ExportedStopKind.Dropped, r.Kind);
        Assert.Null(r.StopId);
        Assert.NotNull(r.DropReason);
    }

    [Fact]
    public void A_stale_FK_falls_back_to_the_same_version_key_rather_than_dropping()
    {
        // RawStopEntityId points at a stop not in the export (999), but the stop time's own
        // (version, id) still names an exported stop → recovered, not dropped.
        var r = Resolver().Resolve(rawStopEntityId: 999, canonicalStationId: null, feedVersionId: 1, rawStopId: "S1");

        Assert.Equal(ExportedStopKind.RawStop, r.Kind);
        Assert.Equal(ExportedStopId.Encode(1, "S1"), r.StopId);
    }

    [Fact]
    public void The_report_tallies_each_arm_per_feed_version_and_flags_drops()
    {
        var resolver = Resolver();
        var report = new ResolutionReport();

        // Version 1: one clean same-version-key resolution.
        report.Record(1, resolver.Resolve(null, null, 1, "S1"));
        // Version 3: one canonical resolution and one drop.
        report.Record(3, resolver.Resolve(null, 100, 3, "elsewhere"));
        report.Record(3, resolver.Resolve(null, null, 3, "unknown"));

        Assert.Equal(1, report.ByFeedVersion[1].ResolvedViaRawStop);
        Assert.False(report.ByFeedVersion[1].Dropped > 0);

        Assert.Equal(1, report.ByFeedVersion[3].ResolvedViaCanonicalStation);
        Assert.Equal(1, report.ByFeedVersion[3].Dropped);
        Assert.NotEmpty(report.ByFeedVersion[3].DropReasonSamples);

        Assert.True(report.AnyDropped);
        Assert.Equal(1, report.TotalDropped);
    }
}
