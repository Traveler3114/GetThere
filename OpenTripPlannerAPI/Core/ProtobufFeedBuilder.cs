using OpenTripPlannerAPI.Scrapers.Hzpp;
using transit_realtime;

namespace OpenTripPlannerAPI.Core;

public static class ProtobufFeedBuilder
{
    public static FeedMessage BuildFromStopTimeUpdates(Dictionary<string, List<StopTimeUpdateDto>> updatesMap)
    {
        var feed = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Incrementality = FeedHeader.Types.Incrementality.FullDataset,
                Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        };

        foreach (var (tripId, stus) in updatesMap)
        {
            if (stus.Count == 0) continue;
            var entity = new FeedEntity
            {
                Id = tripId,
                TripUpdate = new TripUpdate
                {
                    Trip = new TripDescriptor { TripId = tripId }
                }
            };

            foreach (var stu in stus)
            {
                var hasScheduledArrival = stu.ScheduledArrivalSec >= 0;
                var hasScheduledDeparture = stu.ScheduledDepartureSec >= 0;

                if (!hasScheduledArrival && !hasScheduledDeparture)
                    continue;

                var stopTimeUpdate = new TripUpdate.Types.StopTimeUpdate
                {
                    StopSequence = (uint)stu.StopSequence,
                    StopId = stu.StopId
                };

                if (hasScheduledArrival)
                    stopTimeUpdate.Arrival = new TripUpdate.Types.StopTimeEvent { Delay = stu.DelaySec };

                if (hasScheduledDeparture)
                    stopTimeUpdate.Departure = new TripUpdate.Types.StopTimeEvent { Delay = stu.DelaySec };

                entity.TripUpdate.StopTimeUpdate.Add(stopTimeUpdate);
            }

            if (entity.TripUpdate.StopTimeUpdate.Count > 0)
                feed.Entity.Add(entity);
        }

        return feed;
    }
}
