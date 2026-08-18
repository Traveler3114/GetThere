using TransitInfoAPI.Routing.Export;

using TransitRealtime;

namespace GetThere.Tests.Routing;

/// <summary>
/// The RT re-serve step is what keeps live delays attached to the right trips: the export renamed
/// every id per feed version, so an un-rewritten RT feed matches zero trips in OTP and silently shows
/// no delays. These pin that the protobuf output carries the namespaced ids, by parsing it back.
/// </summary>
public class GtfsRealtimeReserializerTests
{
    [Fact]
    public void Rewrites_trip_and_stop_ids_to_the_bundle_namespace()
    {
        var feeds = new List<RtFeedInput>
        {
            new(FeedVersionId: 47, TripUpdates: new List<RtTripUpdateInput>
            {
                new(
                    TripId: "T1",
                    DirectionId: 0,
                    StartTime: "08:00:00",
                    StopTimeUpdates: new List<RtStopTimeUpdateInput>
                    {
                        new(StopSequence: 3, StopId: "S1", DelaySeconds: 120, EstimatedTimeUnix: 1_700_000_000),
                    }),
            }),
        };

        var bytes = GtfsRealtimeReserializer.Build(feeds);
        var message = FeedMessage.Parser.ParseFrom(bytes);

        var entity = Assert.Single(message.Entity);
        var trip = entity.TripUpdate.Trip;
        Assert.Equal("47:T1", trip.TripId);           // namespaced trip id
        Assert.Equal(0u, trip.DirectionId);
        Assert.Equal("08:00:00", trip.StartTime);

        var stu = Assert.Single(entity.TripUpdate.StopTimeUpdate);
        Assert.Equal(3u, stu.StopSequence);
        Assert.Equal("47:S1", stu.StopId);            // namespaced stop id
        Assert.Equal(120, stu.Departure.Delay);
        Assert.Equal(120, stu.Arrival.Delay);
        Assert.Equal(1_700_000_000, stu.Departure.Time);

        Assert.Equal(FeedHeader.Types.Incrementality.FullDataset, message.Header.Incrementality);
    }

    [Fact]
    public void No_updates_produces_a_valid_empty_feed()
    {
        var bytes = GtfsRealtimeReserializer.Build([]);
        var message = FeedMessage.Parser.ParseFrom(bytes);

        Assert.Empty(message.Entity);
        Assert.Equal("2.0", message.Header.GtfsRealtimeVersion);
    }
}
