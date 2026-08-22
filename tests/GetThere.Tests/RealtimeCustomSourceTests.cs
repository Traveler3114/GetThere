using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace GetThere.Tests;

/// <summary>
/// Realtime and mobility custom sources are polled by their own workers and never enter the transit
/// graph. The invariant lives in <see cref="FeedManager.GetActiveImportableFeedsAsync"/>, so these
/// call it rather than asserting that an importer nobody ran produced no rows — which would pass
/// with the exclusion deleted.
/// </summary>
public class RealtimeCustomSourceTests
{
    private static TransitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TransitDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// Only <c>db</c> and <c>importOptions</c> are touched on this path — the constructor assigns the
    /// rest and reads <c>importOptions.Value</c>. Passing null for the others keeps the test to the
    /// query under test instead of standing up twelve collaborators.
    /// </summary>
    private static FeedManager NewManager(TransitDbContext db) =>
        new(db, NullLogger<FeedManager>.Instance, null!, null!, null!, null!, null!, null!, null!,
            Options.Create(new FeedImportOptions()), null!, null!);

    private static async Task<Operator> SeedOperatorAsync(TransitDbContext db)
    {
        var op = new Operator
        {
            GlobalId = "gt-test",
            OnestopId = "o-test",
            Name = "Test",
            ShortName = "test",
            CreatedAt = DateTime.UtcNow
        };
        db.Operators.Add(op);
        await db.SaveChangesAsync();
        return op;
    }

    private static async Task<Feed> AddCustomSourceFeedAsync(
        TransitDbContext db, Operator op, string feedId, bool producesRealtime, bool producesMobility)
    {
        var source = new CustomSource
        {
            OperatorId = op.Id,
            Name = feedId,
            Kind = CustomSourceKind.Http,
            ProducesRealtime = producesRealtime,
            ProducesMobility = producesMobility,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.CustomSources.Add(source);
        await db.SaveChangesAsync();

        var feed = new Feed
        {
            OnestopId = "f-" + feedId,
            FeedId = feedId,
            FeedType = FeedType.GTFSStatic,
            IsActive = true,
            RefreshIntervalSeconds = 3600,
            OperatorId = op.Id,
            CustomSourceId = source.Id,
            Provenance = SourceProvenance.Official
        };
        db.Feeds.Add(feed);
        await db.SaveChangesAsync();
        return feed;
    }

    [Fact]
    public async Task Realtime_custom_source_is_not_importable()
    {
        await using var db = NewDb();
        var op = await SeedOperatorAsync(db);
        await AddCustomSourceFeedAsync(db, op, "rt-source", producesRealtime: true, producesMobility: false);

        var importable = await NewManager(db).GetActiveImportableFeedsAsync(CancellationToken.None);

        Assert.DoesNotContain(importable, f => f.FeedId == "rt-source");
    }

    [Fact]
    public async Task Mobility_custom_source_is_not_importable()
    {
        await using var db = NewDb();
        var op = await SeedOperatorAsync(db);
        await AddCustomSourceFeedAsync(db, op, "mob-source", producesRealtime: false, producesMobility: true);

        var importable = await NewManager(db).GetActiveImportableFeedsAsync(CancellationToken.None);

        Assert.DoesNotContain(importable, f => f.FeedId == "mob-source");
    }

    [Fact]
    public async Task An_ordinary_custom_source_is_still_importable()
    {
        // The other half of the invariant: the exclusion must not swallow every custom source, which
        // is how a "fix" to the query above would most plausibly go wrong.
        await using var db = NewDb();
        var op = await SeedOperatorAsync(db);
        await AddCustomSourceFeedAsync(db, op, "plain-source", producesRealtime: false, producesMobility: false);

        var importable = await NewManager(db).GetActiveImportableFeedsAsync(CancellationToken.None);

        Assert.Contains(importable, f => f.FeedId == "plain-source");
    }

    [Fact]
    public async Task A_plain_gtfs_feed_is_still_importable()
    {
        await using var db = NewDb();
        var op = await SeedOperatorAsync(db);
        db.Feeds.Add(new Feed
        {
            OnestopId = "f-gtfs",
            FeedId = "gtfs-source",
            FeedType = FeedType.GTFSStatic,
            Url = "https://operator.test/gtfs.zip",
            IsActive = true,
            RefreshIntervalSeconds = 3600,
            OperatorId = op.Id,
            Provenance = SourceProvenance.Official
        });
        await db.SaveChangesAsync();

        var importable = await NewManager(db).GetActiveImportableFeedsAsync(CancellationToken.None);

        Assert.Contains(importable, f => f.FeedId == "gtfs-source");
    }

    [Fact]
    public async Task An_inactive_feed_is_not_importable()
    {
        await using var db = NewDb();
        var op = await SeedOperatorAsync(db);
        var feed = await AddCustomSourceFeedAsync(db, op, "off-source", producesRealtime: false, producesMobility: false);
        feed.IsActive = false;
        await db.SaveChangesAsync();

        var importable = await NewManager(db).GetActiveImportableFeedsAsync(CancellationToken.None);

        Assert.DoesNotContain(importable, f => f.FeedId == "off-source");
    }
}
