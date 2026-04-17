using Microsoft.EntityFrameworkCore;
using OpenTripPlannerAPI.Data;
using System.Text.Json;

namespace OpenTripPlannerAPI.Services;

public sealed class DbBackedOtpConfigLoader
{
    private readonly IDbContextFactory<OtpReadDbContext> _dbContextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbBackedOtpConfigLoader> _logger;
    private readonly DbBackedOtpConfigState _state;
    private readonly IHttpClientFactory _httpClientFactory;

    public DbBackedOtpConfigLoader(
        IDbContextFactory<OtpReadDbContext> dbContextFactory,
        IConfiguration configuration,
        ILogger<DbBackedOtpConfigLoader> logger,
        DbBackedOtpConfigState state,
        IHttpClientFactory httpClientFactory)
    {
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
        _logger = logger;
        _state = state;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DbBackedOtpConfigResult> LoadAndGenerateAsync(CancellationToken ct = default)
    {
        var localHzppRealtimeUrl = _configuration["OperatorSource:HzppFallbackRealtimeUrl"] ?? "http://127.0.0.1:5000/rt/hzpp";
        _logger.LogInformation("Using OTP config source: DB");

        OtpConfigArtifacts artifacts;
        try
        {
            artifacts = await BuildArtifactsFromDatabaseAsync(localHzppRealtimeUrl, ct);
        }
        catch (Exception ex)
        {
            artifacts = LoadExistingConfigOrThrow("DB source", localHzppRealtimeUrl, ex);
        }

        await WriteConfigFilesAsync(artifacts, ct);
        _state.UsesLocalHzppScraper = artifacts.UsesLocalHzppScraper;
        _state.LocalHzppStaticGtfsUrl = artifacts.LocalHzppStaticGtfsUrl;

        _logger.LogInformation("Loaded {Count} operators from DB.", artifacts.OperatorsCount);
        return new DbBackedOtpConfigResult { UsesLocalHzppScraper = artifacts.UsesLocalHzppScraper };
    }

    private async Task<OtpConfigArtifacts> BuildArtifactsFromDatabaseAsync(string localHzppRealtimeUrl, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var operators = await db.TransitOperators
            .AsNoTracking()
            .Include(o => o.Country)
            .OrderBy(o => o.Country!.Name)
            .ThenBy(o => o.Name)
            .Select(o => new OtpOperatorFeedDto
            {
                OperatorId = o.Id,
                OperatorName = o.Name,
                CountryId = o.CountryId,
                CountryName = o.Country!.Name,
                FeedId = $"op{o.Id}",
                StaticGtfsUrl = o.GtfsFeedUrl,
                GtfsRealtimeUrl = o.GtfsRealtimeFeedUrl
            })
            .ToListAsync(ct);

        return await BuildArtifactsAsync(operators, localHzppRealtimeUrl, ct);
    }

    private async Task<OtpConfigArtifacts> BuildArtifactsAsync(List<OtpOperatorFeedDto> operators, string localHzppRealtimeUrl, CancellationToken ct)
    {
        if (operators.Count == 0)
            throw new InvalidOperationException("No operators with static GTFS URL were returned from the source.");

        foreach (var op in operators)
        {
            op.GtfsRealtimeUrl = NormalizeLegacyRealtimeUrl(op.GtfsRealtimeUrl, localHzppRealtimeUrl);
        }

        await ValidateOperatorsAsync(operators, ct);
        var frequency = _configuration["OperatorSource:UpdaterFrequency"] ?? "PT30S";

        var buildConfig = new OtpBuildConfig
        {
            transitFeeds = operators.Select(o => new OtpTransitFeedConfig
            {
                type = "gtfs",
                source = o.StaticGtfsUrl!,
                feedId = o.FeedId
            }).ToList(),
            transitModelTimeZone = _configuration["OperatorSource:TransitModelTimeZone"] ?? "Europe/Zagreb"
        };

        var updaters = new List<OtpRouterUpdaterConfig>();
        var usesLocalHzppScraper = false;
        string? localHzppStaticGtfsUrl = null;

        foreach (var op in operators)
        {
            if (string.IsNullOrWhiteSpace(op.GtfsRealtimeUrl))
                continue;

            updaters.Add(new OtpRouterUpdaterConfig
            {
                type = "STOP_TIME_UPDATER",
                feedId = op.FeedId,
                url = op.GtfsRealtimeUrl,
                frequency = frequency
            });

            if (UrlsEqual(op.GtfsRealtimeUrl, localHzppRealtimeUrl))
            {
                usesLocalHzppScraper = true;
                localHzppStaticGtfsUrl ??= op.StaticGtfsUrl;
            }
        }

        return new OtpConfigArtifacts
        {
            BuildConfig = buildConfig,
            RouterConfig = new OtpRouterConfig { updaters = updaters },
            OperatorsCount = operators.Count,
            UsesLocalHzppScraper = usesLocalHzppScraper,
            LocalHzppStaticGtfsUrl = localHzppStaticGtfsUrl
        };
    }

    private async Task ValidateOperatorsAsync(List<OtpOperatorFeedDto> operators, CancellationToken ct)
    {
        foreach (var op in operators)
        {
            if (string.IsNullOrWhiteSpace(op.FeedId))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has empty feed ID.");

            if (!IsValidHttpUrl(op.StaticGtfsUrl))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has invalid static GTFS URL.");

            if (!string.IsNullOrWhiteSpace(op.GtfsRealtimeUrl) && !IsValidHttpUrl(op.GtfsRealtimeUrl))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has invalid GTFS-RT URL.");
        }

        var isStrictReachabilityEnabled = bool.TryParse(_configuration["OperatorSource:StrictReachabilityChecks"], out var enabled) && enabled;
        if (!isStrictReachabilityEnabled)
            return;

        using var client = _httpClientFactory.CreateClient();
        foreach (var op in operators)
        {
            var urlsToProbe = new List<string>();
            if (!string.IsNullOrWhiteSpace(op.StaticGtfsUrl))
                urlsToProbe.Add(op.StaticGtfsUrl);
            if (!string.IsNullOrWhiteSpace(op.GtfsRealtimeUrl))
                urlsToProbe.Add(op.GtfsRealtimeUrl);

            foreach (var url in urlsToProbe)
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Reachability check failed ({(int)response.StatusCode}) for URL '{url}' (operator '{op.OperatorName}').");
            }
        }
    }

    private async Task WriteConfigFilesAsync(OtpConfigArtifacts artifacts, CancellationToken ct)
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var outputDir = AppContext.BaseDirectory;

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "build-config.json"),
            JsonSerializer.Serialize(artifacts.BuildConfig, jsonOptions),
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "router-config.json"),
            JsonSerializer.Serialize(artifacts.RouterConfig, jsonOptions),
            ct);
    }

    private OtpConfigArtifacts LoadExistingConfigOrThrow(string sourceDescription, string localHzppRealtimeUrl, Exception ex)
    {
        var outputDir = AppContext.BaseDirectory;
        var currentDir = Directory.GetCurrentDirectory();
        var buildConfigPath = ResolveExistingConfigPath("build-config.json", outputDir, currentDir);
        var routerConfigPath = ResolveExistingConfigPath("router-config.json", outputDir, currentDir);

        if (buildConfigPath is null || routerConfigPath is null)
        {
            throw new InvalidOperationException(
                $"Failed to load operator feed config from '{sourceDescription}'. Error: {ex.Message}",
                ex);
        }

        var localHzppFeedId = TryResolveLocalHzppFeedId(routerConfigPath, localHzppRealtimeUrl);
        var usesLocalHzppScraper = !string.IsNullOrWhiteSpace(localHzppFeedId);
        var localHzppStaticGtfsUrl = usesLocalHzppScraper ? TryResolveStaticGtfsUrl(buildConfigPath, localHzppFeedId!) : null;
        _state.UsesLocalHzppScraper = usesLocalHzppScraper;
        _state.LocalHzppStaticGtfsUrl = localHzppStaticGtfsUrl;

        _logger.LogWarning(
            ex,
            "Failed to load operator feed config from '{SourceDescription}'. Falling back to existing build/router config files ('{BuildConfigPath}', '{RouterConfigPath}').",
            sourceDescription,
            buildConfigPath,
            routerConfigPath);

        using var buildStream = File.OpenRead(buildConfigPath);
        var buildConfig = JsonSerializer.Deserialize<OtpBuildConfig>(buildStream) ?? new OtpBuildConfig();

        using var routerStream = File.OpenRead(routerConfigPath);
        var routerConfig = JsonSerializer.Deserialize<OtpRouterConfig>(routerStream) ?? new OtpRouterConfig();

        return new OtpConfigArtifacts
        {
            BuildConfig = buildConfig,
            RouterConfig = routerConfig,
            OperatorsCount = buildConfig.transitFeeds.Count,
            UsesLocalHzppScraper = usesLocalHzppScraper,
            LocalHzppStaticGtfsUrl = localHzppStaticGtfsUrl
        };
    }

    private static string? ResolveExistingConfigPath(string fileName, string outputDir, string currentDir)
    {
        var outputPath = Path.Combine(outputDir, fileName);
        if (File.Exists(outputPath))
            return outputPath;

        var currentPath = Path.Combine(currentDir, fileName);
        return File.Exists(currentPath) ? currentPath : null;
    }

    private static string? TryResolveLocalHzppFeedId(string routerConfigPath, string localHzppRealtimeUrl)
    {
        using var stream = File.OpenRead(routerConfigPath);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("updaters", out var updaters) || updaters.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var updater in updaters.EnumerateArray())
        {
            if (!updater.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
                continue;

            var normalized = NormalizeLegacyRealtimeUrl(urlElement.GetString(), localHzppRealtimeUrl);
            if (!UrlsEqual(normalized, localHzppRealtimeUrl))
                continue;

            if (!updater.TryGetProperty("feedId", out var feedIdElement) || feedIdElement.ValueKind != JsonValueKind.String)
                return null;

            return feedIdElement.GetString();
        }

        return null;
    }

    private static string? TryResolveStaticGtfsUrl(string buildConfigPath, string feedId)
    {
        using var stream = File.OpenRead(buildConfigPath);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("transitFeeds", out var transitFeeds) || transitFeeds.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var feed in transitFeeds.EnumerateArray())
        {
            if (!feed.TryGetProperty("feedId", out var feedIdElement) || feedIdElement.ValueKind != JsonValueKind.String)
                continue;

            if (!string.Equals(feedIdElement.GetString(), feedId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!feed.TryGetProperty("source", out var sourceElement) || sourceElement.ValueKind != JsonValueKind.String)
                return null;

            return sourceElement.GetString();
        }

        return null;
    }

    private static string? NormalizeLegacyRealtimeUrl(string? value, string canonicalHzppUrl)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return value;

        if (!parsed.AbsolutePath.Equals("/hzpp-rt", StringComparison.OrdinalIgnoreCase))
            return value;

        return canonicalHzppUrl;
    }

    private static bool IsValidHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
           && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    private static bool UrlsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return Uri.TryCreate(left, UriKind.Absolute, out var l)
            && Uri.TryCreate(right, UriKind.Absolute, out var r)
            && Uri.Compare(l, r, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}

public sealed class DbBackedOtpConfigResult
{
    public bool UsesLocalHzppScraper { get; set; }
}

public sealed class DbBackedOtpConfigState
{
    public bool UsesLocalHzppScraper { get; set; }
    public string? LocalHzppStaticGtfsUrl { get; set; }
}

internal sealed class OtpOperatorFeedDto
{
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public string FeedId { get; set; } = string.Empty;
    public string? StaticGtfsUrl { get; set; }
    public string? GtfsRealtimeUrl { get; set; }
}

internal sealed class OtpConfigArtifacts
{
    public OtpBuildConfig BuildConfig { get; set; } = new();
    public OtpRouterConfig RouterConfig { get; set; } = new();
    public int OperatorsCount { get; set; }
    public bool UsesLocalHzppScraper { get; set; }
    public string? LocalHzppStaticGtfsUrl { get; set; }
}

internal sealed class OtpBuildConfig
{
    public List<OtpTransitFeedConfig> transitFeeds { get; set; } = [];
    public string transitModelTimeZone { get; set; } = "Europe/Zagreb";
}

internal sealed class OtpTransitFeedConfig
{
    public string type { get; set; } = "gtfs";
    public string source { get; set; } = string.Empty;
    public string feedId { get; set; } = string.Empty;
}

internal sealed class OtpRouterConfig
{
    public List<OtpRouterUpdaterConfig> updaters { get; set; } = [];
}

internal sealed class OtpRouterUpdaterConfig
{
    public string type { get; set; } = "STOP_TIME_UPDATER";
    public string feedId { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
    public string frequency { get; set; } = "PT30S";
}
