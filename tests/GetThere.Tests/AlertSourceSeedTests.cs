using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using TransitInfoAPI.Data;
using TransitInfoAPI.Managers;

namespace GetThere.Tests;

public class AlertSourceSeedTests
{
    [Fact]
    public async Task Running_seeder_twice_leaves_nine_alert_source_feeds_not_eighteen()
    {
        var options = new DbContextOptionsBuilder<TransitDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new TransitDbContext(options);
        var onestop = new OnestopIdManager();
        var logger = NullLogger<TransitDataSeeder>.Instance;
        var seeder = new TransitDataSeeder(db, onestop, logger);

        await seeder.SeedAsync(CancellationToken.None);
        var count1 = await db.Feeds.CountAsync(f => f.FeedType == TransitInfoAPI.Enums.FeedType.AlertSource);
        var alertSources1 = await db.AlertSources.CountAsync();

        await seeder.SeedAsync(CancellationToken.None);
        var count2 = await db.Feeds.CountAsync(f => f.FeedType == TransitInfoAPI.Enums.FeedType.AlertSource);
        var alertSources2 = await db.AlertSources.CountAsync();

        Assert.Equal(9, count1);
        Assert.Equal(9, count2);
        Assert.Equal(9, alertSources1);
        Assert.Equal(9, alertSources2);
    }
}
