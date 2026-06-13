using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;

namespace TransitInfoAPI.Services.Otp;

public class OtpManagerService
{
    private readonly TransitDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OtpManagerService> _logger;

    public OtpManagerService(TransitDbContext db, IConfiguration configuration, ILogger<OtpManagerService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task GenerateConfigAsync(CancellationToken ct = default)
    {
        var operators = await _db.Operators
            .Include(o => o.Country)
            .Include(o => o.Feeds)
            .Where(o => o.Feeds.Any(f => f.IsActive))
            .ToListAsync(ct);

        var buildConfig = new OtpBuildConfig
        {
            transitFeeds = operators
                .SelectMany(o => o.Feeds
                    .Where(f => f.IsActive && f.FeedType == Enums.FeedType.GTFSStatic)
                    .Select(f => new OtpTransitFeedConfig
                    {
                        type = "gtfs",
                        source = f.ExternalUrl ?? f.InternalUrl ?? string.Empty,
                        feedId = f.FeedId
                    }))
                .Where(f => !string.IsNullOrWhiteSpace(f.source))
                .ToList(),
            transitModelTimeZone = _configuration["Otp:TimeZone"] ?? "Europe/Zagreb"
        };

        var updaters = operators
            .SelectMany(o => o.Feeds
                .Where(f => f.IsActive && f.FeedType == Enums.FeedType.GTFSRealtime)
                .Select(f => new OtpRouterUpdaterConfig
                {
                    type = "STOP_TIME_UPDATER",
                    feedId = f.FeedId,
                    url = f.InternalUrl ?? f.ExternalUrl ?? string.Empty,
                    frequency = "PT30S"
                }))
            .Where(u => !string.IsNullOrWhiteSpace(u.url))
            .ToList();

        var routerConfig = new OtpRouterConfig { updaters = updaters };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var outputDir = AppContext.BaseDirectory;

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "build-config.json"),
            JsonSerializer.Serialize(buildConfig, jsonOptions), ct);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "router-config.json"),
            JsonSerializer.Serialize(routerConfig, jsonOptions), ct);

        _logger.LogInformation("OTP config generated with {FeedCount} feeds and {UpdaterCount} updaters",
            buildConfig.transitFeeds.Count, updaters.Count);
    }

    public async Task<bool> IsOtpHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var baseUrl = _configuration["Otp:InstanceBaseUrl"] ?? "http://localhost:8080";
            var response = await http.GetAsync($"{baseUrl}/otp/routers/default/index", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
