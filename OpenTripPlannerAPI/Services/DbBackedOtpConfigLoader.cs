using System.Net.Http.Json;
using System.Text.Json;
using OpenTripPlannerAPI.Core;

namespace OpenTripPlannerAPI.Services;

public sealed class DbBackedOtpConfigLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbBackedOtpConfigLoader> _logger;
    private readonly DbBackedOtpConfigState _state;

    public DbBackedOtpConfigLoader(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DbBackedOtpConfigLoader> logger,
        DbBackedOtpConfigState state)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _state = state;
    }

    public async Task<DbBackedOtpConfigResult> LoadAndGenerateAsync(CancellationToken ct = default)
    {
        var apiBaseUrl = _configuration["OperatorSource:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            throw new InvalidOperationException("OperatorSource:ApiBaseUrl must be configured in appsettings.json.");

        var path = _configuration["OperatorSource:OtpFeedsPath"] ?? "/operator/otp-feeds";
        var sourceUrl = $"{apiBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        var localHzppRealtimeUrl = _configuration["OperatorSource:HzppFallbackRealtimeUrl"] ?? "http://127.0.0.1:5000/hzpp-rt";

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

        if (operators.Count == 0)
            throw new InvalidOperationException("No operators with static GTFS URL were returned from the database.");

        await ValidateOperatorsAsync(operators, ct);

        var frequency = _configuration["OperatorSource:UpdaterFrequency"] ?? "PT30S";

        var buildConfig = new
        {
            transitFeeds = operators.Select(o => new
            {
                type = "gtfs",
                source = o.StaticGtfsUrl,
                feedId = o.FeedId
            }).ToList(),
            transitModelTimeZone = _configuration["OperatorSource:TransitModelTimeZone"] ?? "Europe/Zagreb"
        };

        var updaters = new List<object>();
        var localScraperFeedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localScraperStaticGtfsUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var op in operators)
        {
            if (string.IsNullOrWhiteSpace(op.GtfsRealtimeUrl))
                continue;

            updaters.Add(new
            {
                type = "STOP_TIME_UPDATER",
                feedId = op.FeedId,
                url = op.GtfsRealtimeUrl,
                frequency
            });

            if (IsLocalRealtimeUrl(op.GtfsRealtimeUrl, localHzppRealtimeUrl))
            {
                localScraperFeedIds.Add(op.FeedId);
                if (!string.IsNullOrWhiteSpace(op.StaticGtfsUrl))
                    localScraperStaticGtfsUrls[op.FeedId] = op.StaticGtfsUrl;
            }
        }

        var routerConfig = new { updaters };
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var outputDir = AppContext.BaseDirectory;

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "build-config.json"),
            JsonSerializer.Serialize(buildConfig, jsonOptions),
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "router-config.json"),
            JsonSerializer.Serialize(routerConfig, jsonOptions),
            ct);

        _state.SetLocalScraperFeeds(localScraperFeedIds, localScraperStaticGtfsUrls);

        _logger.LogInformation("Generated OTP build-config.json and router-config.json from DB source ({Count} operators).", operators.Count);
        return new DbBackedOtpConfigResult
        {
            LocalScraperFeedIds = localScraperFeedIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private DbBackedOtpConfigResult LoadExistingConfigOrThrow(string sourceUrl, string localHzppRealtimeUrl, HttpRequestException ex)
    {
        var outputDir = AppContext.BaseDirectory;
        var currentDir = Directory.GetCurrentDirectory();

        var buildConfigPath = ResolveExistingConfigPath("build-config.json", outputDir, currentDir);
        var routerConfigPath = ResolveExistingConfigPath("router-config.json", outputDir, currentDir);

        if (buildConfigPath is null || routerConfigPath is null)
        {
            throw new InvalidOperationException(
                $"Failed to fetch operator feed config from '{sourceUrl}'. Status: {ex.StatusCode?.ToString() ?? "unknown"}. Error: {ex.Message}",
                ex);
        }

        var localScraperFeedIds = TryDetectLocalScraperFeedIds(routerConfigPath, localHzppRealtimeUrl);
        var localScraperStaticGtfsUrls = TryResolveStaticGtfsUrls(buildConfigPath, localScraperFeedIds);

        _state.SetLocalScraperFeeds(localScraperFeedIds, localScraperStaticGtfsUrls);

        _logger.LogWarning(
            ex,
            "Failed to fetch operator feed config from '{SourceUrl}'. Falling back to existing build/router config files ('{BuildConfigPath}', '{RouterConfigPath}').",
            sourceUrl,
            buildConfigPath,
            routerConfigPath);

        return new DbBackedOtpConfigResult
        {
            LocalScraperFeedIds = localScraperFeedIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
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

    private static HashSet<string> TryDetectLocalScraperFeedIds(string routerConfigPath, string localHzppRealtimeUrl)
    {
        var feedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var stream = File.OpenRead(routerConfigPath);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("updaters", out var updaters) || updaters.ValueKind != JsonValueKind.Array)
            return feedIds;

        foreach (var updater in updaters.EnumerateArray())
        {
            if (!updater.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
                continue;

            if (!updater.TryGetProperty("feedId", out var feedIdElement) || feedIdElement.ValueKind != JsonValueKind.String)
                continue;

            var feedId = feedIdElement.GetString();
            if (string.IsNullOrWhiteSpace(feedId))
                continue;

            if (IsLocalRealtimeUrl(urlElement.GetString(), localHzppRealtimeUrl))
                feedIds.Add(feedId);
        }

        return feedIds;
    }

    private static Dictionary<string, string> TryResolveStaticGtfsUrls(string buildConfigPath, ISet<string> feedIds)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (feedIds.Count == 0)
            return map;

        using var stream = File.OpenRead(buildConfigPath);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("transitFeeds", out var transitFeeds) || transitFeeds.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var feed in transitFeeds.EnumerateArray())
        {
            if (!feed.TryGetProperty("feedId", out var feedIdElement) || feedIdElement.ValueKind != JsonValueKind.String)
                continue;

            var feedId = feedIdElement.GetString();
            if (string.IsNullOrWhiteSpace(feedId) || !feedIds.Contains(feedId))
                continue;

            if (!feed.TryGetProperty("source", out var sourceElement) || sourceElement.ValueKind != JsonValueKind.String)
                continue;

            var source = sourceElement.GetString();
            if (!string.IsNullOrWhiteSpace(source))
                map[feedId] = source;
        }

        return map;
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

    private static bool IsValidHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
           && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    private static bool IsLocalRealtimeUrl(string? url, string localHzppRealtimeUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (UrlsEqual(url, localHzppRealtimeUrl))
            return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;

        return parsed.IsLoopback && RealtimeRouteConventions.IsLocalRealtimePath(parsed.AbsolutePath);
    }

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
    public List<string> LocalScraperFeedIds { get; set; } = [];
}

public sealed class DbBackedOtpConfigState
{
    private readonly object _lock = new();

    public IReadOnlySet<string> LocalScraperFeedIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> LocalScraperStaticGtfsUrls { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void SetLocalScraperFeeds(ISet<string> feedIds, IDictionary<string, string> staticGtfsUrls)
    {
        lock (_lock)
        {
            LocalScraperFeedIds = new HashSet<string>(feedIds, StringComparer.OrdinalIgnoreCase);
            LocalScraperStaticGtfsUrls = new Dictionary<string, string>(staticGtfsUrls, StringComparer.OrdinalIgnoreCase);
        }
    }
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
