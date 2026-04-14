using System.Net.Http.Json;
using System.Text.Json;

namespace OpenTripPlannerAPI.Services;

public sealed class DbBackedOtpConfigLoader
{
    private const string HzppFallbackMode = "HZPP_Scraper";
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
            throw new InvalidOperationException("OperatorSource:ApiBaseUrl must be configured.");

        var path = _configuration["OperatorSource:OtpFeedsPath"] ?? "/operator/otp-feeds";
        var sourceUrl = $"{apiBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        using var client = _httpClientFactory.CreateClient("operator-source");
        var response = await client.GetAsync(sourceUrl, ct);
        response.EnsureSuccessStatusCode();

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
        var localHzppFallbackUrl = _configuration["OperatorSource:HzppFallbackRealtimeUrl"] ?? "http://127.0.0.1:5000/hzpp-rt";

        var buildConfig = new
        {
            transitFeeds = operators.Select(o => new
            {
                type = "gtfs",
                source = o.StaticGtfsUrl,
                feedId = o.OtpFeedId
            }).ToList(),
            transitModelTimeZone = _configuration["OperatorSource:TransitModelTimeZone"] ?? "Europe/Zagreb"
        };

        var updaters = new List<object>();
        var requiresHzppFallback = false;
        string? hzppFallbackStaticGtfsUrl = null;

        foreach (var op in operators)
        {
            var tripUpdatesUrl = FirstNonEmpty(op.TripUpdatesUrl, op.LegacyGtfsRealtimeUrl);
            if (!string.IsNullOrWhiteSpace(tripUpdatesUrl))
            {
                updaters.Add(new
                {
                    type = "STOP_TIME_UPDATER",
                    feedId = op.OtpFeedId,
                    url = tripUpdatesUrl,
                    frequency
                });
            }
            else if (string.Equals(op.RealtimeFallbackMode, HzppFallbackMode, StringComparison.OrdinalIgnoreCase))
            {
                requiresHzppFallback = true;
                hzppFallbackStaticGtfsUrl ??= op.StaticGtfsUrl;
                updaters.Add(new
                {
                    type = "STOP_TIME_UPDATER",
                    feedId = op.OtpFeedId,
                    url = localHzppFallbackUrl,
                    frequency,
                    fuzzyTripMatching = true
                });
            }

            if (!string.IsNullOrWhiteSpace(op.VehiclePositionsUrl))
            {
                updaters.Add(new
                {
                    type = "VEHICLE_POSITION_UPDATER",
                    feedId = op.OtpFeedId,
                    url = op.VehiclePositionsUrl,
                    frequency
                });
            }

            if (!string.IsNullOrWhiteSpace(op.AlertsUrl))
            {
                updaters.Add(new
                {
                    type = "ALERTS_UPDATER",
                    feedId = op.OtpFeedId,
                    url = op.AlertsUrl,
                    frequency
                });
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

        _state.RequiresHzppFallback = requiresHzppFallback;
        _state.HzppFallbackStaticGtfsUrl = hzppFallbackStaticGtfsUrl;

        _logger.LogInformation("Generated OTP build-config.json and router-config.json from DB source ({Count} operators).", operators.Count);
        return new DbBackedOtpConfigResult { RequiresHzppFallback = requiresHzppFallback };
    }

    private async Task ValidateOperatorsAsync(List<OtpOperatorFeedDto> operators, CancellationToken ct)
    {
        foreach (var op in operators)
        {
            if (string.IsNullOrWhiteSpace(op.OtpFeedId))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has empty OtpFeedId.");

            if (!IsValidHttpUrl(op.StaticGtfsUrl))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has invalid static GTFS URL.");

            var tripUpdatesUrl = FirstNonEmpty(op.TripUpdatesUrl, op.LegacyGtfsRealtimeUrl);
            if (!string.IsNullOrWhiteSpace(tripUpdatesUrl) && !IsValidHttpUrl(tripUpdatesUrl))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has invalid GTFS-RT trip-updates URL.");

            if (!string.IsNullOrWhiteSpace(op.VehiclePositionsUrl) && !IsValidHttpUrl(op.VehiclePositionsUrl))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has invalid GTFS-RT vehicle positions URL.");

            if (!string.IsNullOrWhiteSpace(op.AlertsUrl) && !IsValidHttpUrl(op.AlertsUrl))
                throw new InvalidOperationException($"Operator '{op.OperatorName}' has invalid GTFS-RT alerts URL.");
        }

        var strictProbe = bool.TryParse(_configuration["OperatorSource:StrictReachabilityChecks"], out var enabled) && enabled;
        if (!strictProbe)
            return;

        using var client = _httpClientFactory.CreateClient("operator-source");
        foreach (var op in operators)
        {
            var urlsToProbe = new List<string>();
            if (!string.IsNullOrWhiteSpace(op.StaticGtfsUrl))
                urlsToProbe.Add(op.StaticGtfsUrl);
            var tripUpdatesUrl = FirstNonEmpty(op.TripUpdatesUrl, op.LegacyGtfsRealtimeUrl);
            if (!string.IsNullOrWhiteSpace(tripUpdatesUrl)) urlsToProbe.Add(tripUpdatesUrl);
            if (!string.IsNullOrWhiteSpace(op.VehiclePositionsUrl)) urlsToProbe.Add(op.VehiclePositionsUrl);
            if (!string.IsNullOrWhiteSpace(op.AlertsUrl)) urlsToProbe.Add(op.AlertsUrl);

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

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed class DbBackedOtpConfigResult
{
    public bool RequiresHzppFallback { get; set; }
}

public sealed class DbBackedOtpConfigState
{
    public bool RequiresHzppFallback { get; set; }
    public string? HzppFallbackStaticGtfsUrl { get; set; }
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
    public string OtpFeedId { get; set; } = string.Empty;
    public string OtpInstanceKey { get; set; } = string.Empty;
    public string? StaticGtfsUrl { get; set; }
    public string? LegacyGtfsRealtimeUrl { get; set; }
    public string? TripUpdatesUrl { get; set; }
    public string? VehiclePositionsUrl { get; set; }
    public string? AlertsUrl { get; set; }
    public string RealtimeFallbackMode { get; set; } = "None";
}
