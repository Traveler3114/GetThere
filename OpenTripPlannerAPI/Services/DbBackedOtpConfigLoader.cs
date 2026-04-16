using Microsoft.EntityFrameworkCore;
using OpenTripPlannerAPI.Data;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenTripPlannerAPI.Services;

public sealed class DbBackedOtpConfigLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<OtpReadDbContext> _dbContextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbBackedOtpConfigLoader> _logger;
    private readonly DbBackedOtpConfigState _state;

    public DbBackedOtpConfigLoader(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<OtpReadDbContext> dbContextFactory,
        IConfiguration configuration,
        ILogger<DbBackedOtpConfigLoader> logger,
        DbBackedOtpConfigState state)
    {
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
        _logger = logger;
        _state = state;
    }

    public async Task<DbBackedOtpConfigResult> LoadAndGenerateAsync(CancellationToken ct = default)
    {
        var source = ParseSource(_configuration["OtpConfigSource"]);
        var localHzppRealtimeUrl = _configuration["OperatorSource:HzppFallbackRealtimeUrl"] ?? "http://127.0.0.1:5000/rt/hzpp";
        var compareBoth = bool.TryParse(_configuration["OtpConfigValidation:EnableDualPathComparison"], out var compareEnabled) && compareEnabled;
        var failOnMismatch = bool.TryParse(_configuration["OtpConfigValidation:FailOnMismatch"], out var failEnabled) && failEnabled;

        var sourceLogLabel = source == OtpConfigSource.Database ? "DB" : "HTTP";
        _logger.LogInformation("Using OTP config source: {Source}", sourceLogLabel);

        OtpConfigArtifacts? httpArtifacts = null;
        OtpConfigArtifacts? dbArtifacts = null;

        if (source == OtpConfigSource.Http || compareBoth)
        {
            httpArtifacts = await BuildArtifactsFromHttpAsync(localHzppRealtimeUrl, ct);
        }

        if (source == OtpConfigSource.Database || compareBoth)
        {
            dbArtifacts = await BuildArtifactsFromDatabaseAsync(localHzppRealtimeUrl, ct);
        }

        if (compareBoth)
        {
            if (httpArtifacts is null || dbArtifacts is null)
                throw new InvalidOperationException("Dual-path comparison requires both HTTP and DB artifacts.");

            await WriteConfigFilesAsync(httpArtifacts, ".http", ct);
            await WriteConfigFilesAsync(dbArtifacts, ".db", ct);

            var diff = Compare(httpArtifacts, dbArtifacts);
            if (!diff.AreEquivalent)
            {
                _logger.LogError("OTP config mismatch between HTTP and DB source. {Details}", diff.Description);
                if (failOnMismatch)
                    throw new InvalidOperationException($"OTP config mismatch between HTTP and DB source: {diff.Description}");
            }
            else
            {
                _logger.LogInformation("Dual-path OTP config comparison passed; HTTP and DB outputs are equivalent.");
            }
        }

        var selectedArtifacts = source switch
        {
            OtpConfigSource.Http => httpArtifacts,
            OtpConfigSource.Database => dbArtifacts,
            _ => null
        };

        if (selectedArtifacts is null)
            throw new InvalidOperationException("No OTP config artifacts were generated for the selected source.");

        await WriteConfigFilesAsync(selectedArtifacts, suffix: null, ct);
        _state.UsesLocalHzppScraper = selectedArtifacts.UsesLocalHzppScraper;
        _state.LocalHzppStaticGtfsUrl = selectedArtifacts.LocalHzppStaticGtfsUrl;

        _logger.LogInformation("Loaded {Count} operators from {Source}.", selectedArtifacts.OperatorsCount, sourceLogLabel);
        return new DbBackedOtpConfigResult { UsesLocalHzppScraper = selectedArtifacts.UsesLocalHzppScraper };
    }

    private async Task<OtpConfigArtifacts> BuildArtifactsFromHttpAsync(string localHzppRealtimeUrl, CancellationToken ct)
    {
        var apiBaseUrl = _configuration["OperatorSource:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            throw new InvalidOperationException("OperatorSource:ApiBaseUrl must be configured for HTTP config source.");

        var path = _configuration["OperatorSource:OtpFeedsPath"] ?? "/operator/otp-feeds";
        var sourceUrl = $"{apiBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        using var client = _httpClientFactory.CreateClient("operator-source");
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(sourceUrl, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            return LoadExistingConfigOrThrow(sourceUrl, localHzppRealtimeUrl, ex);
        }

        var payload = await response.Content.ReadFromJsonAsync<OperationResultDto<List<OtpOperatorFeedDto>>>(cancellationToken: ct);
        if (payload is null || !payload.Success || payload.Data is null)
            throw new InvalidOperationException("Failed to load OTP operator feed config from API.");

        var operators = payload.Data
            .Where(x => !string.IsNullOrWhiteSpace(x.StaticGtfsUrl))
            .ToList();

        return await BuildArtifactsAsync(operators, localHzppRealtimeUrl, ct);
    }

    private async Task<OtpConfigArtifacts> BuildArtifactsFromDatabaseAsync(string localHzppRealtimeUrl, CancellationToken ct)
    {
        try
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
        catch (Exception ex)
        {
            return LoadExistingConfigOrThrow("DB source", localHzppRealtimeUrl, ex);
        }
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

        using var client = _httpClientFactory.CreateClient("operator-source");
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

    private async Task WriteConfigFilesAsync(OtpConfigArtifacts artifacts, string? suffix, CancellationToken ct)
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var outputDir = AppContext.BaseDirectory;
        var buildName = suffix is null ? "build-config.json" : $"build-config{suffix}.json";
        var routerName = suffix is null ? "router-config.json" : $"router-config{suffix}.json";

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, buildName),
            JsonSerializer.Serialize(artifacts.BuildConfig, jsonOptions),
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, routerName),
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

    private static OtpConfigDiff Compare(OtpConfigArtifacts httpArtifacts, OtpConfigArtifacts dbArtifacts)
    {
        var httpFeeds = httpArtifacts.BuildConfig.transitFeeds
            .Select(x => $"{x.feedId}|{x.source}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dbFeeds = dbArtifacts.BuildConfig.transitFeeds
            .Select(x => $"{x.feedId}|{x.source}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var httpUpdaters = httpArtifacts.RouterConfig.updaters
            .Select(x => $"{x.feedId}|{x.url}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dbUpdaters = dbArtifacts.RouterConfig.updaters
            .Select(x => $"{x.feedId}|{x.url}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingFeedsInDb = httpFeeds.Except(dbFeeds, StringComparer.OrdinalIgnoreCase).ToList();
        var missingFeedsInHttp = dbFeeds.Except(httpFeeds, StringComparer.OrdinalIgnoreCase).ToList();
        var missingUpdatersInDb = httpUpdaters.Except(dbUpdaters, StringComparer.OrdinalIgnoreCase).ToList();
        var missingUpdatersInHttp = dbUpdaters.Except(httpUpdaters, StringComparer.OrdinalIgnoreCase).ToList();

        if (missingFeedsInDb.Count == 0 && missingFeedsInHttp.Count == 0
            && missingUpdatersInDb.Count == 0 && missingUpdatersInHttp.Count == 0)
        {
            return new OtpConfigDiff { AreEquivalent = true, Description = "No differences." };
        }

        var parts = new List<string>();
        if (missingFeedsInDb.Count > 0)
            parts.Add($"Missing feeds in DB: {string.Join(", ", missingFeedsInDb)}");
        if (missingFeedsInHttp.Count > 0)
            parts.Add($"Missing feeds in HTTP: {string.Join(", ", missingFeedsInHttp)}");
        if (missingUpdatersInDb.Count > 0)
            parts.Add($"Missing updaters in DB: {string.Join(", ", missingUpdatersInDb)}");
        if (missingUpdatersInHttp.Count > 0)
            parts.Add($"Missing updaters in HTTP: {string.Join(", ", missingUpdatersInHttp)}");

        return new OtpConfigDiff
        {
            AreEquivalent = false,
            Description = string.Join(" | ", parts)
        };
    }

    private static OtpConfigSource ParseSource(string? value)
    {
        if (string.Equals(value, "Database", StringComparison.OrdinalIgnoreCase))
            return OtpConfigSource.Database;
        if (string.Equals(value, "Http", StringComparison.OrdinalIgnoreCase))
            return OtpConfigSource.Http;

        return OtpConfigSource.Http;
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

internal sealed class OperationResultDto<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
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

internal enum OtpConfigSource
{
    Http,
    Database
}

internal sealed class OtpConfigArtifacts
{
    public OtpBuildConfig BuildConfig { get; set; } = new();
    public OtpRouterConfig RouterConfig { get; set; } = new();
    public int OperatorsCount { get; set; }
    public bool UsesLocalHzppScraper { get; set; }
    public string? LocalHzppStaticGtfsUrl { get; set; }
}

internal sealed class OtpConfigDiff
{
    public bool AreEquivalent { get; set; }
    public string Description { get; set; } = string.Empty;
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
