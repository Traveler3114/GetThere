using GetThereShared.Dtos;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GetThereAPI.Managers;

/// <summary>
/// Wraps the Transitland v2 REST API.
/// All public methods degrade gracefully when the API key is absent or the
/// network is unavailable — callers always receive an empty collection rather
/// than an exception.
/// </summary>
public class TransitlandManager
{
    // ── DI ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TransitlandManager> _logger;

    public TransitlandManager(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<TransitlandManager> logger)
    {
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logger = logger;
    }

    // ── Constants ─────────────────────────────────────────────────────────

    private const string DefaultRestApiBaseUrl  = "https://transit.land/api/v2/rest";
    private const string DefaultTilesBaseUrl    = "https://transit.land/api/v2/tiles";
    private const string DefaultStopsPath       = "stops";
    private const string DefaultApiKeyQueryName = "apikey";
    private const string DefaultApiKeyHeaderName = "apikey";
    private const int    DefaultStopsLimit      = 5000;

    // ── Static caches (survive per-request DI scoping) ────────────────────

    private static readonly TimeSpan DetailsCacheTtl    = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DeparturesCacheTtl = TimeSpan.FromSeconds(20);

    private static readonly ConcurrentDictionary<string, (DateTime Expires, StopDetailsResult Data)>
        s_detailsCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, (DateTime Expires, List<StopTimeResult> Data)>
        s_departuresCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Country name → ISO-2 lookup ───────────────────────────────────────

    private static readonly Dictionary<string, string> CountryIsoByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Croatia"]     = "HR",
        ["Slovenia"]    = "SI",
        ["Austria"]     = "AT",
        ["Germany"]     = "DE",
        ["France"]      = "FR",
        ["Italy"]       = "IT",
        ["Poland"]      = "PL",
        ["Czechia"]     = "CZ",
        ["Hungary"]     = "HU",
        ["Switzerland"] = "CH",
        ["Slovakia"]    = "SK",
        ["Spain"]       = "ES",
    };


    private static readonly Dictionary<string, string> CountryBbox = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HR"] = "13.5,42.3,19.4,46.6",   // Croatia
        ["SI"] = "13.4,45.4,16.6,46.9",   // Slovenia
        ["AT"] = "9.5,46.4,17.2,49.0",    // Austria
        ["DE"] = "5.9,47.3,15.0,55.1",    // Germany
        ["FR"] = "-5.1,41.3,9.6,51.1",    // France
        ["IT"] = "6.6,36.6,18.5,47.1",    // Italy
        ["PL"] = "14.1,49.0,24.2,54.8",   // Poland
        ["CZ"] = "12.1,48.6,18.9,51.1",   // Czechia
        ["HU"] = "16.1,45.7,22.9,48.6",   // Hungary
        ["CH"] = "5.9,45.8,10.5,47.8",    // Switzerland
        ["SK"] = "16.8,47.7,22.6,49.6",   // Slovakia
        ["ES"] = "-9.3,35.9,4.3,43.8",    // Spain
    };

    // ── Public accessors ──────────────────────────────────────────────────

    public string GetTilesBaseUrl()
        => (_configuration["Transitland:TilesBaseUrl"] ?? DefaultTilesBaseUrl).Trim().TrimEnd('/');

    public string GetApiKey() => ResolveApiKey() ?? string.Empty;

    // ═════════════════════════════════════════════════════════════════════
    // GetStopsAsync — bulk stop list (used to seed the map GeoJSON layer)
    // ═════════════════════════════════════════════════════════════════════

    public async Task<List<StopDto>> GetStopsAsync(string? countryName, CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation(
                "Transitland API key not configured — skipping bulk stops fetch. " +
                "Set Transitland:ApiKey (or TRANSITLAND__APIKEY env var).");
            return [];
        }

        var baseUrl  = Config("Transitland:RestApiBaseUrl", DefaultRestApiBaseUrl).TrimEnd('/');
        var path     = Config("Transitland:StopsPath", DefaultStopsPath).Trim('/');
        var qParam   = Config("Transitland:ApiKeyQueryParam", DefaultApiKeyQueryName);
        var hParam   = Config("Transitland:ApiKeyHeaderParam",
                         Config("Transitland:ApiKeyHeaderName", DefaultApiKeyHeaderName));

        var limit = DefaultStopsLimit;
        if (int.TryParse(_configuration["Transitland:StopsLimit"], out var cfgLimit) && cfgLimit > 0)
            limit = cfgLimit;

        var countryIso = countryName is not null && CountryIsoByName.TryGetValue(countryName, out var iso) ? iso : null;

        var q = new List<string> { $"limit={limit}" };

        if (!string.IsNullOrWhiteSpace(countryIso) && CountryBbox.TryGetValue(countryIso, out var bbox))
        {
            q.Add($"bbox={Uri.EscapeDataString(bbox)}");
        }
        // Remove the adm0_iso / adm0_name params entirely — they don't work

        q.Add($"{Uri.EscapeDataString(qParam)}={Uri.EscapeDataString(apiKey)}");

        var url = $"{baseUrl}/{path}?{string.Join("&", q)}";

        _logger.LogInformation("[DEBUG] Fetching stops URL: {Url}", url);

        try
        {
            using var response = await SendAsync(url, hParam, apiKey, cancellationToken);
            _logger.LogInformation("[DEBUG] Transitland stops response status: {StatusCode}", (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("[DEBUG] Transitland stops request failed ({StatusCode}). Body: {Body}",
                    (int)response.StatusCode, errorBody[..Math.Min(500, errorBody.Length)]);
                return [];
            }

            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("[DEBUG] Transitland raw response length: {Len} chars. Preview: {Preview}",
                rawBody.Length, rawBody[..Math.Min(300, rawBody.Length)]);

            using var json = JsonDocument.Parse(rawBody);

            if (!TryGetArray(json.RootElement, out var arr))
            {
                _logger.LogWarning("[DEBUG] Transitland stops response has no recognisable array payload. Root keys: {Keys}",
                    string.Join(", ", json.RootElement.EnumerateObject().Select(p => p.Name)));
                return [];
            }

            _logger.LogInformation("[DEBUG] Transitland returned {Count} raw stops in array", arr.GetArrayLength());

            var result = new List<StopDto>();
            var skippedNoCoords = 0;
            var skippedNoId = 0;
            var skippedCountryMismatch = 0;

            foreach (var stop in arr.EnumerateArray())
            {
                if (!TryParseCoordinates(stop, out var lat, out var lon))
                {
                    skippedNoCoords++;
                    continue;
                }

                var stopId = GetString(stop, "onestop_id")
                             ?? GetString(stop, "stop_id")
                             ?? GetString(stop, "id");
                if (string.IsNullOrWhiteSpace(stopId))
                {
                    skippedNoId++;
                    continue;
                }

                var stopCountryName = ExtractCountryName(stop);
                var stopCountryIso  = ExtractCountryIso(stop);

                if (!IsCountryMatch(countryName, countryIso, stopCountryName, stopCountryIso))
                {
                    skippedCountryMismatch++;
                    continue;
                }

                result.Add(new StopDto
                {
                    StopId          = stopId,
                    Name            = GetString(stop, "stop_name") ?? GetString(stop, "name") ?? stopId,
                    Lat             = lat,
                    Lon             = lon,
                    RouteType       = ExtractRouteType(stop),
                    SupportsSchedule = true,
                });
            }

            _logger.LogInformation(
                "[DEBUG] Stops parsed: {Count} kept, {NoCoords} skipped(no coords), " +
                "{NoId} skipped(no id), {Mismatch} skipped(country mismatch)",
                result.Count, skippedNoCoords, skippedNoId, skippedCountryMismatch);

            if (result.Count > 0)
                _logger.LogInformation("[DEBUG] First stop: id={Id} name={Name} lat={Lat} lon={Lon}",
                    result[0].StopId, result[0].Name, result[0].Lat, result[0].Lon);



            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DEBUG] Transitland bulk stops request failed");
            return [];
        }
    }

    public async Task<List<StopDto>> GetStopsByBboxAsync(
        string bbox,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        var baseUrl = Config("Transitland:RestApiBaseUrl", DefaultRestApiBaseUrl).TrimEnd('/');
        var path = Config("Transitland:StopsPath", DefaultStopsPath).Trim('/');
        var qParam = Config("Transitland:ApiKeyQueryParam", DefaultApiKeyQueryName);
        var hParam = Config("Transitland:ApiKeyHeaderParam",
                        Config("Transitland:ApiKeyHeaderName", DefaultApiKeyHeaderName));

        // Query each route type separately so we know the type per stop
        var routeTypes = new[] { (0, "tram"), (2, "rail"), (3, "bus") };
        var allStops = new List<StopDto>();
        var seen = new HashSet<string>();

        var tasks = routeTypes.Select(async rt =>
        {
            var (routeType, _) = rt;
            var url = $"{baseUrl}/{path}?bbox={Uri.EscapeDataString(bbox)}" +
                      $"&served_by_route_type={routeType}" +
                      $"&limit=500" +
                      $"&{Uri.EscapeDataString(qParam)}={Uri.EscapeDataString(apiKey)}";

            _logger.LogInformation("[DEBUG] Viewport stops ({Type}): {Url}", routeType, url);

            try
            {
                using var response = await SendAsync(url, hParam, apiKey, cancellationToken);
                if (!response.IsSuccessStatusCode) return (routeType, new List<StopDto>());

                var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
                using var json = JsonDocument.Parse(rawBody);
                if (!TryGetArray(json.RootElement, out var arr))
                    return (routeType, new List<StopDto>());

                var stops = new List<StopDto>();
                foreach (var stop in arr.EnumerateArray())
                {
                    if (!TryParseCoordinates(stop, out var lat, out var lon)) continue;

                    var stopId = GetString(stop, "onestop_id") ?? GetString(stop, "stop_id");
                    if (string.IsNullOrWhiteSpace(stopId)) continue;

                    // Skip non-physical stops
                    if (stop.TryGetProperty("location_type", out var lt) &&
                        lt.ValueKind == JsonValueKind.Number &&
                        lt.GetInt32() != 0) continue;

                    stops.Add(new StopDto
                    {
                        StopId = stopId,
                        Name = GetString(stop, "stop_name") ?? stopId,
                        Lat = lat,
                        Lon = lon,
                        RouteType = routeType,  // ← we know this exactly
                        SupportsSchedule = true,
                    });
                }

                _logger.LogInformation("[DEBUG] Viewport stops type={Type}: {Count} returned",
                    routeType, stops.Count);
                return (routeType, stops);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Viewport stops request failed for route type {Type}", routeType);
                return (routeType, new List<StopDto>());
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (_, stops) in results)
            foreach (var stop in stops)
                if (seen.Add(stop.StopId))
                    allStops.Add(stop);

        _logger.LogInformation("[DEBUG] Viewport stops total: {Count}", allStops.Count);
        return allStops;
    }

    // ═════════════════════════════════════════════════════════════════════
    // GetStopDetailsAsync — single stop name + served routes
    // Cached for 5 minutes.
    // ═════════════════════════════════════════════════════════════════════

    public async Task<StopDetailsResult?> GetStopDetailsAsync(
        string stopId,
        CancellationToken cancellationToken = default)
    {
        if (s_detailsCache.TryGetValue(stopId, out var hit) && hit.Expires > DateTime.UtcNow)
            return hit.Data;

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var baseUrl = Config("Transitland:RestApiBaseUrl", DefaultRestApiBaseUrl).TrimEnd('/');
        var qParam = Config("Transitland:ApiKeyQueryParam", DefaultApiKeyQueryName);
        var hParam = Config("Transitland:ApiKeyHeaderParam",
                        Config("Transitland:ApiKeyHeaderName", DefaultApiKeyHeaderName));

        var url = $"{baseUrl}/stops/{Uri.EscapeDataString(stopId)}" +
                  $"?{Uri.EscapeDataString(qParam)}={Uri.EscapeDataString(apiKey)}";

        CancellationTokenSource? cts = null;

        try
        {
            cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            using var response = await SendAsync(url, hParam, apiKey, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transitland stop details request failed ({StatusCode}) for {StopId}",
                    (int)response.StatusCode, stopId);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            var stop = GetFirstStop(json.RootElement);
            if (stop is null) return null;

            var name = GetString(stop.Value, "stop_name")
                       ?? GetString(stop.Value, "name")
                       ?? stopId;

            var routes = new List<(string Id, string ShortName, int RouteType)>();

            if (stop.Value.TryGetProperty("routes_serving_stop", out var rss) && rss.ValueKind == JsonValueKind.Array)
            {
                foreach (var rssItem in rss.EnumerateArray())
                {
                    var route = rssItem.TryGetProperty("route", out var r) ? r : rssItem;

                    var rId = GetString(route, "route_id") ?? GetString(route, "id") ?? "";
                    var rName = GetString(route, "route_short_name") ?? "";
                    TryGetInt(route, "route_type", out var rType);

                    if (!string.IsNullOrEmpty(rId))
                        routes.Add((rId, rName, rType));
                }
            }

            var result = new StopDetailsResult(name, routes);

            s_detailsCache[stopId] = (DateTime.UtcNow + DetailsCacheTtl, result);

            return result;
        }
        catch (OperationCanceledException) when (cts?.IsCancellationRequested == true)
        {
            _logger.LogWarning("Stop details request timed out for {StopId}", stopId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transitland stop details request failed for {StopId}", stopId);
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // GetStopDeparturesAsync — next N departures for a stop
    // Includes realtime delay when available.
    // Cached for 20 seconds so the 30-second auto-refresh always gets
    // fresh data without flooding the upstream API.
    // ═════════════════════════════════════════════════════════════════════

    public async Task<List<StopTimeResult>> GetStopDeparturesAsync(
        string stopId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (s_departuresCache.TryGetValue(stopId, out var hit) && hit.Expires > DateTime.UtcNow)
            return hit.Data;

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        var baseUrl = Config("Transitland:RestApiBaseUrl", DefaultRestApiBaseUrl).TrimEnd('/');
        var qParam  = Config("Transitland:ApiKeyQueryParam", DefaultApiKeyQueryName);
        var hParam  = Config("Transitland:ApiKeyHeaderParam",
                        Config("Transitland:ApiKeyHeaderName", DefaultApiKeyHeaderName));

        var url = $"{baseUrl}/stops/{Uri.EscapeDataString(stopId)}/departures" +
                  $"?limit={limit}&{Uri.EscapeDataString(qParam)}={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var response = await SendAsync(url, hParam, apiKey, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transitland departures request failed ({StatusCode}) for {StopId}",
                    (int)response.StatusCode, stopId);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var stop = GetFirstStop(json.RootElement);
            if (stop is null) return [];

            if (!stop.Value.TryGetProperty("stop_times", out var stopTimes)
                || stopTimes.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<StopTimeResult>();

            foreach (var st in stopTimes.EnumerateArray())
            {
                // ── trip metadata ──────────────────────────────────────
                if (!st.TryGetProperty("trip", out var trip)) continue;

                var tripId   = GetString(trip, "trip_id") ?? "";
                var headsign = GetString(trip, "trip_headsign") ?? "";

                string routeId = "", routeShortName = "", routeLongName = "";
                int    routeType = 3;

                if (trip.TryGetProperty("route", out var routeEl))
                {
                    routeId        = GetString(routeEl, "route_id") ?? "";
                    routeShortName = GetString(routeEl, "route_short_name") ?? "";
                    routeLongName  = GetString(routeEl, "route_long_name") ?? "";
                    TryGetInt(routeEl, "route_type", out routeType);
                }

                if (string.IsNullOrEmpty(headsign)) headsign = routeLongName;

                // ── departure times ────────────────────────────────────
                if (!st.TryGetProperty("departure", out var dep)) continue;

                var scheduledTime = ParseLocalTime(dep, "scheduled_local");
                if (string.IsNullOrEmpty(scheduledTime)) continue;

                var  estimatedTime = ParseLocalTime(dep, "estimated_local");
                int? delayMinutes  = null;
                bool isRealtime    = !string.IsNullOrEmpty(estimatedTime);

                if (isRealtime)
                {
                    if (TryGetInt(dep, "estimated_delay_seconds", out var ds))
                        delayMinutes = ds / 60;
                    else if (TryGetInt(dep, "estimated_delay", out var ds2))
                        delayMinutes = ds2 / 60;
                }

                // Don't show estimated when it equals scheduled (no delay info)
                if (estimatedTime == scheduledTime) estimatedTime = null;

                result.Add(new StopTimeResult(
                    TripId:         tripId,
                    RouteId:        routeId,
                    RouteShortName: routeShortName,
                    Headsign:       headsign,
                    RouteType:      routeType,
                    ScheduledTime:  scheduledTime,
                    EstimatedTime:  estimatedTime,
                    DelayMinutes:   delayMinutes,
                    IsRealtime:     isRealtime));
            }

            s_departuresCache[stopId] = (DateTime.UtcNow + DeparturesCacheTtl, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transitland departures request failed for {StopId}", stopId);
            return [];
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Result types (used by OperatorManager to normalise into shared DTOs)
    // ═════════════════════════════════════════════════════════════════════

    public sealed record StopDetailsResult(
        string Name,
        List<(string Id, string ShortName, int RouteType)> Routes);

    public sealed record StopTimeResult(
        string  TripId,
        string  RouteId,
        string  RouteShortName,
        string  Headsign,
        int     RouteType,
        string  ScheduledTime,
        string? EstimatedTime,
        int?    DelayMinutes,
        bool    IsRealtime);

    // ═════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═════════════════════════════════════════════════════════════════════

    private string Config(string key, string fallback)
        => _configuration[key]?.Trim() is { Length: > 0 } v ? v : fallback;

    private string? ResolveApiKey()
    {
        const string key = "Transitland:ApiKey";
        if (_configuration is not IConfigurationRoot root)
            return _configuration[key]?.Trim();

        var emptyHigherPriority = new List<string>();
        var providers = root.Providers as IList<IConfigurationProvider> ?? root.Providers.ToList();
        for (var i = providers.Count - 1; i >= 0; i--)
        {
            var provider = providers[i];
            if (!provider.TryGet(key, out var value)) continue;

            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                if (emptyHigherPriority.Count > 0)
                    _logger.LogWarning(
                        "Transitland API key is blank in higher-priority source(s): {Sources}. Using lower-priority value.",
                        string.Join(", ", emptyHigherPriority));
                return trimmed;
            }

            emptyHigherPriority.Add(provider.ToString() ?? provider.GetType().Name);
        }

        return null;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string url, string headerName, string apiKey, CancellationToken ct)
    {
        using var client  = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(headerName, apiKey);
        return await client.SendAsync(request, ct);
    }

    // ── JSON array extraction ─────────────────────────────────────────────

    private static bool TryGetArray(JsonElement root, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Array) { array = root; return true; }
        foreach (var key in new[] { "stops", "data", "items" })
        {
            if (root.TryGetProperty(key, out array) && array.ValueKind == JsonValueKind.Array)
                return true;
        }
        array = default;
        return false;
    }

    private static JsonElement? GetFirstStop(JsonElement root)
    {
        if (!TryGetArray(root, out var arr) || arr.GetArrayLength() == 0)
            return null;
        return arr[0];
    }

    // ── Coordinate parsing ────────────────────────────────────────────────

    private static bool TryParseCoordinates(JsonElement stop, out double lat, out double lon)
    {
        lat = lon = 0;

        if (stop.TryGetProperty("geometry", out var geo)
            && geo.TryGetProperty("coordinates", out var coords)
            && coords.ValueKind == JsonValueKind.Array
            && coords.GetArrayLength() >= 2
            && TryGetDouble(coords[0], out lon)
            && TryGetDouble(coords[1], out lat))
            return true;

        if (TryGetDouble(stop, "lat",       out lat) && TryGetDouble(stop, "lon",       out lon)) return true;
        if (TryGetDouble(stop, "latitude",  out lat) && TryGetDouble(stop, "longitude", out lon)) return true;

        return false;
    }

    // ── Time parsing ──────────────────────────────────────────────────────

    private static string? ParseLocalTime(JsonElement element, string propertyName)
    {
        var raw = GetString(element, propertyName);
        if (string.IsNullOrEmpty(raw)) return null;

        // "HH:MM:SS" or "HH:MM" — hours can exceed 23 for after-midnight trips
        var parts = raw.Split(':');
        if (parts.Length < 2) return null;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return null;

        return $"{h % 24:D2}:{m:D2}";
    }

    // ── Route type extraction ─────────────────────────────────────────────

    private static int ExtractRouteType(JsonElement stop)
    {
        if (TryGetInt(stop, "route_type", out var t)) return t;

        if (stop.TryGetProperty("route_types", out var rts) && rts.ValueKind == JsonValueKind.Array)
            foreach (var item in rts.EnumerateArray())
                if (TryGetInt(item, out var t2)) return t2;

        foreach (var key in new[] { "routes_serving_stop", "routes", "route_stops" })
        {
            if (!stop.TryGetProperty(key, out var routes) || routes.ValueKind != JsonValueKind.Array) continue;
            foreach (var route in routes.EnumerateArray())
            {
                if (TryGetInt(route, "route_type", out var rt)) return rt;
                if (route.TryGetProperty("route", out var nestedRoute)
                    && TryGetInt(nestedRoute, "route_type", out var nestedRt))
                    return nestedRt;
            }
        }

        return 3; // default: bus
    }

    // ── Country matching ──────────────────────────────────────────────────

    private static string? ExtractCountryName(JsonElement stop)
    {
        // Transitland v2 uses adm0_name; keep older fallbacks for other data sources
        var v = GetString(stop, "adm0_name") ?? GetString(stop, "country_name") ?? GetString(stop, "country");
        if (!string.IsNullOrWhiteSpace(v)) return v;
        if (stop.TryGetProperty("country", out var co) && co.ValueKind == JsonValueKind.Object)
            return GetString(co, "name");
        return null;
    }

    private static string? ExtractCountryIso(JsonElement stop)
    {
        // Transitland v2 uses adm0_iso; keep older fallbacks for other data sources
        var v = GetString(stop, "adm0_iso") ?? GetString(stop, "country_code") ?? GetString(stop, "country_iso");
        if (!string.IsNullOrWhiteSpace(v)) return v;
        if (stop.TryGetProperty("country", out var co) && co.ValueKind == JsonValueKind.Object)
            return GetString(co, "iso_code") ?? GetString(co, "id");
        return null;
    }

    private static bool IsCountryMatch(
        string? reqName, string? reqIso, string? stopName, string? stopIso)
    {
        // No filter requested — accept everything
        if (string.IsNullOrWhiteSpace(reqName) && string.IsNullOrWhiteSpace(reqIso)) return true;

        // Filter requested but stop has no country info — with bbox filtering
        // the stop is already geographically correct, so accept it
        if (string.IsNullOrWhiteSpace(stopName) && string.IsNullOrWhiteSpace(stopIso)) return true;

        if (!string.IsNullOrWhiteSpace(reqIso) && !string.IsNullOrWhiteSpace(stopIso)
            && string.Equals(reqIso, stopIso, StringComparison.OrdinalIgnoreCase)) return true;

        if (!string.IsNullOrWhiteSpace(reqName) && !string.IsNullOrWhiteSpace(stopName)
            && string.Equals(reqName, stopName, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    // ── Low-level JSON readers ────────────────────────────────────────────

    private static string? GetString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString()
             : v.ValueKind == JsonValueKind.Number ? v.GetRawText()
             : null;
    }

    private static bool TryGetDouble(JsonElement el, string prop, out double v)
    {
        v = 0;
        return el.TryGetProperty(prop, out var raw) && TryGetDouble(raw, out v);
    }

    private static bool TryGetDouble(JsonElement el, out double v)
    {
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetDouble(out v);
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out v)) return true;
        v = 0; return false;
    }

    private static bool TryGetInt(JsonElement el, string prop, out int v)
    {
        v = 0;
        return el.TryGetProperty(prop, out var raw) && TryGetInt(raw, out v);
    }

    private static bool TryGetInt(JsonElement el, out int v)
    {
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetInt32(out v);
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out v)) return true;
        v = 0; return false;
    }
}
