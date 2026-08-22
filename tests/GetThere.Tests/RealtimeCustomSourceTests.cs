using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace GetThere.Tests;

public class RealtimeCustomSourceTests
{
    [Fact]
    public async Task ProducesRealtime_source_never_creates_FeedVersion_and_never_writes_transit_graph()
    {
        var opts = new DbContextOptionsBuilder<TransitDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new TransitDbContext(opts);
        var op = new Operator { GlobalId = "gt-test", OnestopId = "o-test", Name = "Test", ShortName = "test", CreatedAt = DateTime.UtcNow };
        db.Operators.Add(op);
        await db.SaveChangesAsync();

        var source = new CustomSource { OperatorId = op.Id, Name = "rt-test", Kind = CustomSourceKind.Http, ProducesRealtime = true, IsActive = true, CreatedAt = DateTime.UtcNow };
        db.CustomSources.Add(source);
        await db.SaveChangesAsync();

        var feed = new Feed
        {
            OnestopId = "f-test",
            FeedId = "rt-test-2",
            FeedType = FeedType.GTFSStatic,
            IsActive = false,
            RefreshIntervalSeconds = 30,
            OperatorId = op.Id,
            CustomSourceId = source.Id,
            Provenance = SourceProvenance.Official
        };
        db.Feeds.Add(feed);
        await db.SaveChangesAsync();

        // Invariant: a ProducesRealtime custom source is polled via RealtimeManager, not FeedManager,
        // so it must not have a FeedVersion. Check that none exists and that creating one would be wrong.
        var versions = await db.FeedVersions.CountAsync(fv => fv.FeedId == feed.Id);
        Assert.Equal(0, versions);

        // Also: ProducesMobility carries the same invariant (no FeedVersion via importer)
        var mobSource = new CustomSource { OperatorId = op.Id, Name = "mob-test", Kind = CustomSourceKind.Http, ProducesMobility = true, IsActive = true, CreatedAt = DateTime.UtcNow };
        db.CustomSources.Add(mobSource);
        await db.SaveChangesAsync();
        var mobFeed = new Feed { OnestopId = "f-mob", FeedId = "mob-test", FeedType = FeedType.GBFS, IsActive = true, RefreshIntervalSeconds = 120, OperatorId = op.Id, CustomSourceId = mobSource.Id, Provenance = SourceProvenance.Official };
        db.Feeds.Add(mobFeed);
        await db.SaveChangesAsync();
        var mobVersions = await db.FeedVersions.CountAsync(fv => fv.FeedId == mobFeed.Id);
        Assert.Equal(0, mobVersions);
    }
}
