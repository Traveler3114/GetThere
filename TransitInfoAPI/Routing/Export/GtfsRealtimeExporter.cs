using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Routing.Export;

/// <summary>
/// Serves the ingested GTFS-RT trip updates to OTP as one normalized protobuf feed, with ids
/// rewritten to the export bundle (<see cref="GtfsRealtimeReserializer"/>). No second upstream fetch:
/// <see cref="RealtimeManager"/> already polls each RT feed once, and this projects that cache.
/// <para>
/// The namespacing needs, per RT feed, the active static feed version its ids belong to. A feed
/// belongs to an operator (<c>Feed.OperatorId</c>), so an RT feed and its static counterpart are the
/// same operator — this resolves the operator's active static <c>FeedVersion</c> and namespaces with
/// it. An operator with no active static version (RT arriving before any timetable) is logged and
/// omitted rather than emitted with unmatchable ids.
/// </para>
/// </summary>
public sealed class GtfsRealtimeExporter(
    TransitDbContext db,
    RealtimeManager realtime,
    ILogger<GtfsRealtimeExporter> logger)
{
    public async Task<byte[]> ExportAsync(CancellationToken ct = default)
    {
        var updatesByFeed = realtime.GetTripUpdatesByFeed();
        if (updatesByFeed.Count == 0)
            return GtfsRealtimeReserializer.Build([]);

        var operatorByRtFeed = await db.Feeds.AsNoTracking()
            .Where(f => f.FeedType == FeedType.GTFSRealtime)
            .Select(f => new { f.Id, f.OperatorId })
            .ToDictionaryAsync(f => f.Id, f => f.OperatorId, ct);

        // Active static feed version per operator — the namespace an operator's RT ids map onto.
        var staticVersions = await (
            from f in db.Feeds
            where f.IsActive && f.FeedType == FeedType.GTFSStatic
            from v in f.FeedVersions
            where v.IsActive
            select new { f.OperatorId, VersionId = v.Id }).ToListAsync(ct);
        var versionByOperator = staticVersions
            .GroupBy(x => x.OperatorId)
            .ToDictionary(g => g.Key, g => g.First().VersionId);

        var inputs = new List<RtFeedInput>();
        foreach (var (feedId, updates) in updatesByFeed)
        {
            if (!operatorByRtFeed.TryGetValue(feedId, out var operatorId))
                continue;
            if (!versionByOperator.TryGetValue(operatorId, out var versionId))
            {
                logger.LogWarning(
                    "GTFS-RT feed {FeedId} (operator {OperatorId}) has no active static feed version to namespace against; its {Count} update(s) are omitted from the OTP feed.",
                    feedId, operatorId, updates.Count);
                continue;
            }

            inputs.Add(new RtFeedInput(versionId, updates.Select(ToInput).ToList()));
        }

        return GtfsRealtimeReserializer.Build(inputs);
    }

    private static RtTripUpdateInput ToInput(Contracts.TripUpdateResponse tu) => new(
        TripId: tu.TripId,
        DirectionId: tu.DirectionId,
        StartTime: tu.StartTime,
        StopTimeUpdates: tu.StopTimeUpdates
            .Select(s => new RtStopTimeUpdateInput(s.StopSequence, s.StopId, s.DelaySeconds, s.EstimatedTime))
            .ToList());
}
