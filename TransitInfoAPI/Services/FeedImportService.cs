using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services;

public class FeedImportService
{
    private readonly TransitDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FeedImportService> _logger;
    private readonly IWebHostEnvironment _env;

    public FeedImportService(TransitDbContext db, IHttpClientFactory httpFactory, ILogger<FeedImportService> logger, IWebHostEnvironment env)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
        _env = env;
    }

    public async Task<Feed> RegisterFeedAsync(int operatorId, FeedType feedType, SourceType sourceType, string feedId, string? externalUrl, string? internalUrl, int refreshIntervalSeconds, CancellationToken ct = default)
    {
        var feed = new Feed
        {
            OperatorId = operatorId,
            FeedType = feedType,
            SourceType = sourceType,
            FeedId = feedId,
            ExternalUrl = externalUrl,
            InternalUrl = internalUrl,
            RefreshIntervalSeconds = refreshIntervalSeconds,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Feeds.Add(feed);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Registered feed {FeedId} for operator {OperatorId}", feedId, operatorId);
        return feed;
    }

    public async Task ImportGtfsStaticAsync(int feedId, CancellationToken ct = default)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { feedId }, ct);
        if (feed is null || feed.FeedType != FeedType.GTFSStatic)
            return;

        var url = feed.ExternalUrl ?? feed.InternalUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        var http = _httpFactory.CreateClient();
        var bytes = await http.GetByteArrayAsync(url, ct);

        var outputDir = Path.Combine(_env.ContentRootPath, "feeds", feed.FeedId);
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, "gtfs.zip");
        await File.WriteAllBytesAsync(outputPath, bytes, ct);

        feed.LastFetched = DateTime.UtcNow;
        feed.LastSuccessful = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Imported GTFS static for feed {FeedId} ({Size} bytes)", feed.FeedId, bytes.Length);
    }

    public async Task<List<Feed>> GetFeedsDueForImportAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Feeds
            .Where(f => f.IsActive && f.FeedType == FeedType.GTFSStatic)
            .Where(f => f.LastFetched == null || EF.Functions.DateDiffSecond(f.LastFetched.Value, now) >= f.RefreshIntervalSeconds)
            .ToListAsync(ct);
    }

    public async Task DeactivateFeedAsync(int feedId, CancellationToken ct = default)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { feedId }, ct);
        if (feed is not null)
        {
            feed.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
    }
}
