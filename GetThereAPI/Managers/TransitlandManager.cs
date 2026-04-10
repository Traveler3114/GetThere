using GetThereShared.Dtos;
using System.Text.Json;

namespace GetThereAPI.Managers;

public class TransitlandManager
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TransitlandManager> _logger;

    private const string DefaultRestApiBaseUrl = "https://transit.land/api/v2/rest";
    private const string DefaultTilesStyleUrl = "https://tiles.transit.land/styles/transit/style.json";
    private const string DefaultStopsPath = "stops";
    private const string DefaultApiKeyQueryName = "apikey";
    private const string DefaultApiKeyHeaderName = "apikey";
    private const int DefaultLimit = 5000;

    private static readonly Dictionary<string, string> CountryIsoByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Croatia"] = "HR",
        ["Slovenia"] = "SI",
        ["Austria"] = "AT",
        ["Germany"] = "DE",
        ["France"] = "FR",
        ["Italy"] = "IT",
        ["Poland"] = "PL",
        ["Czechia"] = "CZ",
        ["Hungary"] = "HU",
        ["Switzerland"] = "CH",
        ["Slovakia"] = "SK",
        ["Spain"] = "ES"
    };

    public TransitlandManager(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<TransitlandManager> logger)
    {
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<StopDto>> GetStopsAsync(string? countryName, CancellationToken cancellationToken = default)
    {
        var baseUrl = (_configuration["Transitland:RestApiBaseUrl"] ?? DefaultRestApiBaseUrl).TrimEnd('/');
        var stopsPath = (_configuration["Transitland:StopsPath"] ?? DefaultStopsPath).Trim('/');
        var apiKey = ResolveApiKey();
        var apiKeyParam = _configuration["Transitland:ApiKeyQueryParam"] ?? DefaultApiKeyQueryName;
        var apiKeyHeader = _configuration["Transitland:ApiKeyHeaderParam"]
                           ?? _configuration["Transitland:ApiKeyHeaderName"]
                           ?? DefaultApiKeyHeaderName;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation(
                "Transitland API key is not configured; skipping stops request. Set Transitland:ApiKey (or TRANSITLAND__APIKEY).");
            return [];
        }

        var limit = DefaultLimit;
        if (int.TryParse(_configuration["Transitland:StopsLimit"], out var configuredLimit) && configuredLimit > 0)
            limit = configuredLimit;

        var countryIso = countryName is not null && CountryIsoByName.TryGetValue(countryName, out var iso)
            ? iso
            : null;

        var query = new List<string>
        {
            $"limit={limit}"
        };

        if (!string.IsNullOrWhiteSpace(countryIso))
            query.Add($"country={Uri.EscapeDataString(countryIso)}");
        else if (!string.IsNullOrWhiteSpace(countryName))
            query.Add($"country_name={Uri.EscapeDataString(countryName)}");

        query.Add($"{Uri.EscapeDataString(apiKeyParam)}={Uri.EscapeDataString(apiKey)}");

        var url = $"{baseUrl}/{stopsPath}?{string.Join("&", query)}";

        try
        {
            using var client = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(apiKeyHeader, apiKey);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transitland stops request failed with status {StatusCode}", (int)response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!TryGetStopsArray(json.RootElement, out var stopsArray))
            {
                _logger.LogWarning("Transitland stops response missing expected array payload");
                return [];
            }

            var result = new List<StopDto>();
            foreach (var stop in stopsArray.EnumerateArray())
            {
                if (!TryParseCoordinates(stop, out var lat, out var lon))
                    continue;

                var stopId = GetString(stop, "onestop_id")
                             ?? GetString(stop, "stop_id")
                             ?? GetString(stop, "id");
                if (string.IsNullOrWhiteSpace(stopId))
                {
                    _logger.LogWarning("Transitland stop skipped because no stable identifier was provided");
                    continue;
                }

                var parsedCountryName = ExtractCountryName(stop);
                var parsedCountryIso = ExtractCountryIso(stop);

                if (!IsCountryMatch(countryName, countryIso, parsedCountryName, parsedCountryIso))
                    continue;

                result.Add(new StopDto
                {
                    StopId = stopId,
                    Name = GetString(stop, "name") ?? stopId,
                    Lat = lat,
                    Lon = lon,
                    RouteType = ExtractRouteType(stop),
                    SupportsSchedule = false
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transitland stops request failed");
            return [];
        }
    }

    public string GetTilesStyleUrl()
    {
        var styleUrl = (_configuration["Transitland:TilesStyleUrl"] ?? DefaultTilesStyleUrl).Trim();
        var apiKey = ResolveApiKey();
        var apiKeyParam = _configuration["Transitland:ApiKeyQueryParam"] ?? DefaultApiKeyQueryName;
        var allowWithoutApiKey = bool.TryParse(_configuration["Transitland:AllowTilesWithoutApiKey"], out var value) && value;

        if (string.IsNullOrWhiteSpace(apiKey))
            return allowWithoutApiKey ? styleUrl : string.Empty;

        var separator = styleUrl.Contains('?') ? "&" : "?";
        return $"{styleUrl}{separator}{Uri.EscapeDataString(apiKeyParam)}={Uri.EscapeDataString(apiKey)}";
    }

    private string? ResolveApiKey()
    {
        const string key = "Transitland:ApiKey";
        if (_configuration is not IConfigurationRoot root)
            return _configuration[key]?.Trim();

        var emptyHigherPriorityProviders = new List<string>();
        var providers = root.Providers as IList<IConfigurationProvider> ?? root.Providers.ToList();
        for (var i = providers.Count - 1; i >= 0; i--)
        {
            var provider = providers[i];
            if (!provider.TryGet(key, out var value))
                continue;

            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                if (emptyHigherPriorityProviders.Count > 0)
                {
                    _logger.LogWarning(
                        "Transitland API key is empty in higher-priority config source(s): {Sources}. Falling back to lower-priority configured value.",
                        string.Join(", ", emptyHigherPriorityProviders));
                }

                return trimmed;
            }

            emptyHigherPriorityProviders.Add(provider.ToString() ?? provider.GetType().Name);
        }

        return null;
    }

    private static bool TryGetStopsArray(JsonElement root, out JsonElement stopsArray)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            stopsArray = root;
            return true;
        }

        if (root.TryGetProperty("stops", out stopsArray) && stopsArray.ValueKind == JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("data", out stopsArray) && stopsArray.ValueKind == JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("items", out stopsArray) && stopsArray.ValueKind == JsonValueKind.Array)
            return true;

        stopsArray = default;
        return false;
    }

    private static bool TryParseCoordinates(JsonElement stop, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;

        if (stop.TryGetProperty("geometry", out var geometry)
            && geometry.TryGetProperty("coordinates", out var coordinates)
            && coordinates.ValueKind == JsonValueKind.Array
            && coordinates.GetArrayLength() >= 2)
        {
            var lonRaw = coordinates[0];
            var latRaw = coordinates[1];
            if (TryGetDouble(latRaw, out lat) && TryGetDouble(lonRaw, out lon))
                return true;
        }

        if (TryGetDouble(stop, "lat", out lat) && TryGetDouble(stop, "lon", out lon))
            return true;
        if (TryGetDouble(stop, "latitude", out lat) && TryGetDouble(stop, "longitude", out lon))
            return true;

        return false;
    }

    private static int ExtractRouteType(JsonElement stop)
    {
        if (TryGetInt(stop, "route_type", out var directType))
            return directType;

        if (stop.TryGetProperty("route_types", out var routeTypes) && routeTypes.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in routeTypes.EnumerateArray())
                if (TryGetInt(item, out var type))
                    return type;
        }

        foreach (var key in new[] { "routes_serving_stop", "routes", "route_stops" })
        {
            if (!stop.TryGetProperty(key, out var routes) || routes.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var route in routes.EnumerateArray())
            {
                if (TryGetInt(route, "route_type", out var routeType))
                    return routeType;
            }
        }

        // GTFS route_type fallback: 3 = bus.
        // Used when Transitland payload does not expose any route type hints.
        return 3;
    }

    private static string? ExtractCountryName(JsonElement stop)
    {
        var direct = GetString(stop, "country_name") ?? GetString(stop, "country");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (stop.TryGetProperty("country", out var countryObj) && countryObj.ValueKind == JsonValueKind.Object)
            return GetString(countryObj, "name");

        return null;
    }

    private static string? ExtractCountryIso(JsonElement stop)
    {
        var direct = GetString(stop, "country_code") ?? GetString(stop, "country_iso");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (stop.TryGetProperty("country", out var countryObj) && countryObj.ValueKind == JsonValueKind.Object)
            return GetString(countryObj, "iso_code") ?? GetString(countryObj, "id");

        return null;
    }

    private static bool IsCountryMatch(
        string? requestedName,
        string? requestedIso,
        string? stopCountryName,
        string? stopCountryIso)
    {
        if (string.IsNullOrWhiteSpace(requestedName) && string.IsNullOrWhiteSpace(requestedIso))
            return true;

        // If payload has no country metadata, keep item (API-side filter should already apply).
        if (string.IsNullOrWhiteSpace(stopCountryName) && string.IsNullOrWhiteSpace(stopCountryIso))
            return true;

        if (!string.IsNullOrWhiteSpace(requestedIso) && !string.IsNullOrWhiteSpace(stopCountryIso)
            && string.Equals(requestedIso, stopCountryIso, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(requestedName) && !string.IsNullOrWhiteSpace(stopCountryName)
            && string.Equals(requestedName, stopCountryName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind == JsonValueKind.Number
                ? value.GetRawText()
                : null;
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var raw))
            return false;
        return TryGetDouble(raw, out value);
    }

    private static bool TryGetDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDouble(out value);

        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(element.GetString(), out value))
            return true;

        value = 0;
        return false;
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var raw))
            return false;
        return TryGetInt(raw, out value);
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt32(out value);

        if (element.ValueKind == JsonValueKind.String
            && int.TryParse(element.GetString(), out value))
            return true;

        value = 0;
        return false;
    }
}
