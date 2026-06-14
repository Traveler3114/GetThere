using System.IO.Compression;

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

        // Flatten nested GTFS zip (some providers put files in a subfolder)
        await FlattenGtfsZipAsync(outputPath, ct);

        feed.LastFetched = DateTime.UtcNow;
        feed.LastSuccessful = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Imported GTFS static for feed {FeedId} ({Size} bytes)", feed.FeedId, bytes.Length);
    }

    private static readonly HashSet<string> DedupFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "agency.txt", "stops.txt", "routes.txt", "pathways.txt", "levels.txt", "calendar.txt"
    };

    private static async Task FlattenGtfsZipAsync(string zipPath, CancellationToken ct)
    {
        bool needsFlatten;
        string? prefix;

        using (var original = ZipFile.OpenRead(zipPath))
        {
            needsFlatten = !original.Entries.Any(e => e.FullName.Equals("agency.txt", StringComparison.OrdinalIgnoreCase));

            prefix = null;
            if (needsFlatten)
            {
                prefix = original.Entries
                    .Select(e => Path.GetDirectoryName(e.FullName))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .GroupBy(d => d)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;
                if (string.IsNullOrEmpty(prefix))
                    needsFlatten = false;
            }

            var tempPath = zipPath + ".tmp";
            using (var temp = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                foreach (var entry in original.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.Length == 0) continue;
                    var name = needsFlatten
                        ? entry.FullName.Substring(prefix!.Length + 1)
                        : entry.FullName;

                    if (DedupFiles.Contains(Path.GetFileName(name)))
                    {
                        var newEntry = temp.CreateEntry(name, CompressionLevel.Optimal);
                        using var dst = newEntry.Open();
                        using var src = entry.Open();
                        using var reader = new StreamReader(src);
                        using var writer = new StreamWriter(dst);
                        var seen = new HashSet<string>();
                        string? line;
                        while ((line = reader.ReadLine()) is not null)
                        {
                            var firstCol = line.Split(',')[0].Trim('"');
                            if (seen.Add(firstCol))
                                await writer.WriteLineAsync(line);
                        }
                    }
                    else
                    {
                        var newEntry = temp.CreateEntry(name, CompressionLevel.Optimal);
                        using var src = entry.Open();
                        using var dst = newEntry.Open();
                        await src.CopyToAsync(dst, ct);
                    }
                }
            }
        }

        // original is disposed here, safe to delete
        File.Delete(zipPath);
        File.Move(zipPath + ".tmp", zipPath);
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
