using Google.Protobuf;

using TransitRealtime;

namespace TransitInfoAPI.Routing.Export;

/// <summary>One trip's live update, keyed by the operator's <b>original</b> trip/stop ids.</summary>
public sealed record RtTripUpdateInput(
    string TripId,
    int? DirectionId,
    string? StartTime,
    IReadOnlyList<RtStopTimeUpdateInput> StopTimeUpdates);

public sealed record RtStopTimeUpdateInput(int? StopSequence, string? StopId, int DelaySeconds, long? EstimatedTimeUnix);

/// <summary>
/// A feed's updates plus the active static feed version its ids belong to — the number needed to
/// namespace them onto the export bundle.
/// </summary>
public sealed record RtFeedInput(int FeedVersionId, IReadOnlyList<RtTripUpdateInput> TripUpdates);

/// <summary>
/// Re-serializes ingested GTFS-RT trip updates into a single normalized protobuf feed for OTP, with
/// every entity id <b>rewritten to the bundle's namespaced form</b> (<see cref="ExportedStopId"/>).
/// This is the real translation step the plan calls out: the export renamed <c>T1</c> to
/// <c>{version}:T1</c>, so an un-rewritten RT feed would match zero trips in OTP's graph and apply no
/// delays — indistinguishable from "no delays". Because the export keeps each raw id beside its
/// namespaced form, the rewrite is a deterministic encode, not a heuristic.
/// <para>
/// Pure and OTP-free: given already-resolved feed inputs it produces bytes, so the encode is
/// unit-testable by parsing the result back.
/// </para>
/// </summary>
public static class GtfsRealtimeReserializer
{
    public static byte[] Build(IReadOnlyList<RtFeedInput> feeds)
    {
        var message = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Incrementality = FeedHeader.Types.Incrementality.FullDataset,
                Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
        };

        foreach (var feed in feeds)
        {
            foreach (var tu in feed.TripUpdates)
            {
                var trip = new TripDescriptor
                {
                    // Namespaced so it matches the exported trip_id. RouteId is deliberately omitted:
                    // the RT feed carries the operator's raw route id, but the bundle keys routes by
                    // canonical OnestopId, and OTP resolves an update by trip_id anyway.
                    TripId = ExportedStopId.Encode(feed.FeedVersionId, tu.TripId),
                };
                if (tu.DirectionId is int dir and >= 0)
                    trip.DirectionId = (uint)dir;
                if (!string.IsNullOrEmpty(tu.StartTime))
                    trip.StartTime = tu.StartTime;

                var tripUpdate = new TripUpdate { Trip = trip };

                foreach (var stu in tu.StopTimeUpdates)
                {
                    var update = new TripUpdate.Types.StopTimeUpdate();
                    if (stu.StopSequence is int seq and >= 0)
                        update.StopSequence = (uint)seq;
                    if (!string.IsNullOrEmpty(stu.StopId))
                        update.StopId = ExportedStopId.Encode(feed.FeedVersionId, stu.StopId);

                    var evt = new TripUpdate.Types.StopTimeEvent { Delay = stu.DelaySeconds };
                    if (stu.EstimatedTimeUnix is long t)
                        evt.Time = t;

                    // GTFS-RT lets a delay stand for both arrival and departure; setting both keeps
                    // consumers that read only one of them correct.
                    update.Arrival = evt;
                    update.Departure = evt.Clone();
                    tripUpdate.StopTimeUpdate.Add(update);
                }

                message.Entity.Add(new FeedEntity
                {
                    Id = $"{feed.FeedVersionId}:{tu.TripId}",
                    TripUpdate = tripUpdate,
                });
            }
        }

        return message.ToByteArray();
    }
}
