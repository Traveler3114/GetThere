using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using GetThereAPI.Common;
using GetThereAPI.Exceptions;

using GetThereShared.Common;

using Microsoft.Extensions.Options;

using static System.FormattableString;

namespace GetThereAPI.Services;

public class TransitInfoApiOptions
{
    /// <summary>Matches the HTTPS endpoint in TransitInfoAPI's Kestrel configuration.</summary>
    public string BaseUrl { get; set; } = "https://localhost:5001";
    public string ClientId { get; set; } = "getthere-api@transit.local";
    public string ClientSecret { get; set; } = "";
}

public class TransitInfoApiClient
{
    /// <summary>Upstream page size for list endpoints. Results beyond this are not fetched.</summary>
    private const int UpstreamPageSize = 500;

    /// <summary>
    /// Ceiling on how many items a single paged read will accumulate. Guards against pulling an
    /// entire national feed into memory when a query is too broad.
    /// </summary>
    private const int MaxUpstreamItems = 10_000;

    /// <summary>Radius used for the vehicle bounding box when the caller does not supply one.</summary>
    private const double DefaultVehicleRadiusKm = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TransitInfoApiOptions _options;
    private readonly ILogger<TransitInfoApiClient> _logger;
    private static string? _cachedAccessToken;
    private static DateTime _tokenExpiry;
    private static readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    public TransitInfoApiClient(HttpClient httpClient, IOptions<TransitInfoApiOptions> options, ILogger<TransitInfoApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    /// <summary>Clears the shared token under the same lock that writes it.</summary>
    private static void InvalidateCachedToken()
    {
        _tokenSemaphore.Wait();
        try
        {
            _cachedAccessToken = null;
            _tokenExpiry = default;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_cachedAccessToken != null && _tokenExpiry > DateTime.UtcNow.AddMinutes(5))
            return _cachedAccessToken;

        await _tokenSemaphore.WaitAsync(ct);
        try
        {
            if (_cachedAccessToken != null && _tokenExpiry > DateTime.UtcNow.AddMinutes(5))
                return _cachedAccessToken;

            var loginRequest = new
            {
                Email = _options.ClientId,
                Password = _options.ClientSecret
            };

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync("/auth/login", loginRequest, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // TransitInfoAPI unreachable or timed out. That is an upstream failure, not a fault
                // in this API, so it must not surface as a bare 500.
                _logger.LogError(ex, "Could not reach TransitInfoAPI to authenticate");
                throw new AppException("Transit information service is unavailable.", 502, "TRANSIT_UPSTREAM_UNAVAILABLE");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("TransitInfoAPI rejected the service account credentials ({Status})", (int)response.StatusCode);
                response.Dispose();
                throw new AppException("Transit information service is unavailable.", 502, "TRANSIT_UPSTREAM_UNAVAILABLE");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var loginResult = JsonSerializer.Deserialize<LoginResult>(json, JsonOptions);

            _cachedAccessToken = loginResult?.AccessToken
                ?? throw new AppException("Transit information service is unavailable.", 502, "TRANSIT_UPSTREAM_UNAVAILABLE");
            _tokenExpiry = GetTokenExpiry(_cachedAccessToken);
            _logger.LogInformation("Obtained new access token from TransitInfoAPI, expires at {Expiry}", _tokenExpiry);

            return _cachedAccessToken;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    /// <summary>
    /// Builds a lat/lon/radius query string. Coordinates are formatted with the invariant culture —
    /// interpolating a double under a comma-decimal culture (hr-HR) emits "lat=45,8", which the
    /// upstream API cannot parse.
    /// </summary>
    private static List<string> BuildGeoQuery(double? lat, double? lon, double? radiusKm)
    {
        List<string> query = [];
        if (lat.HasValue) query.Add(Invariant($"lat={lat.Value}"));
        if (lon.HasValue) query.Add(Invariant($"lon={lon.Value}"));
        if (radiusKm.HasValue) query.Add(Invariant($"radiusKm={radiusKm.Value}"));
        query.Add(Invariant($"perPage={UpstreamPageSize}"));
        return query;
    }

    /// <summary>
    /// Forwards a GET to TransitInfoAPI and returns the response body untouched, along with its
    /// content type. Used by the map proxy: the map page renders GeoJSON and other upstream shapes
    /// directly, and re-modelling them here would only add drift.
    /// </summary>
    public async Task<(string Body, string ContentType)> GetRawAsync(string pathAndQuery, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not reach TransitInfoAPI for {Path}", pathAndQuery);
            throw new AppException("Transit information service is unavailable.", 502, "TRANSIT_UPSTREAM_UNAVAILABLE");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("TransitInfoAPI returned 401 for {Path}, refreshing token and retrying", pathAndQuery);
            InvalidateCachedToken();
            token = await GetAccessTokenAsync(ct);

            using var retry = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response.Dispose();
            response = await _httpClient.SendAsync(retry, ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("TransitInfoAPI returned {Status} for {Path}", (int)response.StatusCode, pathAndQuery);
                throw new AppException("Transit information service is unavailable.", 502, "TRANSIT_UPSTREAM_UNAVAILABLE");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            return (body, contentType);
        }
    }

    private async Task<T> SendWithAuthAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("TransitInfoAPI returned 401, refreshing token and retrying {Method} {Path}", request.Method, request.RequestUri?.PathAndQuery);
            InvalidateCachedToken();
            token = await GetAccessTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await _httpClient.SendAsync(request, ct);
        }

        // An upstream failure is not this API's fault — surface it as 502 rather than letting
        // HttpRequestException bubble to the handler and become a bare 500.
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("TransitInfoAPI returned {Status} for {Method} {Path}",
                (int)response.StatusCode, request.Method, request.RequestUri?.PathAndQuery);
            throw new AppException("Transit information service is unavailable.", 502, "TRANSIT_UPSTREAM_UNAVAILABLE");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new AppException("Transit information service returned an unreadable response.", 502, "TRANSIT_UPSTREAM_UNAVAILABLE");
    }

    /// <summary>
    /// Reads every page of a paginated upstream list, up to <see cref="MaxUpstreamItems"/>.
    /// <para>
    /// These calls used to request a single page of 500 and return it, silently dropping anything
    /// beyond that — a city with more than 500 stations simply lost the rest with no error and no log
    /// line. Paging to exhaustion is the fix; the cap stops a pathological feed pulling everything
    /// into memory, and is logged when it bites.
    /// </para>
    /// </summary>
    private async Task<List<T>> FetchAllPagesAsync<T>(string path, List<string> query, CancellationToken ct)
    {
        List<T> all = [];
        var page = 1;

        while (true)
        {
            var paged = new List<string>(query) { Invariant($"page={page}") };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?{string.Join("&", paged)}");
            var result = await SendWithAuthAsync<PaginatedResponse<T>>(request, ct);

            if (result.Data.Count == 0) break;

            all.AddRange(result.Data);

            if (all.Count >= MaxUpstreamItems)
            {
                _logger.LogWarning(
                    "Truncating {Path} at {Count} items (upstream reports {Total}) — raise MaxUpstreamItems or narrow the query",
                    path, all.Count, result.Total);
                break;
            }

            if (result.Total > 0 && all.Count >= result.Total) break;
            if (result.Data.Count < UpstreamPageSize) break;

            page++;
        }

        return all;
    }

    public async Task<List<TransitStationResponse>> GetStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
        => await FetchAllPagesAsync<TransitStationResponse>("/stations", BuildGeoQuery(lat, lon, radiusKm), ct);

    public async Task<List<TransitRouteResponse>> GetRoutesAsync(int? operatorId, string? routeType, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (operatorId.HasValue) query.Add(Invariant($"operatorId={operatorId.Value}"));
        if (!string.IsNullOrEmpty(routeType)) query.Add($"routeType={Uri.EscapeDataString(routeType)}");
        query.Add(Invariant($"perPage={UpstreamPageSize}"));

        return await FetchAllPagesAsync<TransitRouteResponse>("/routes", query, ct);
    }

    public async Task<List<TransitVehicleResponse>> GetVehiclesAsync(string? feedId, double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(feedId)) query.Add($"feedId={Uri.EscapeDataString(feedId)}");

        // The box is centred on the point. Adding the offset to both edges (as this once did) produced
        // a box extending only north-east, so vehicles south or west of the caller were never returned.
        if (lat.HasValue || lon.HasValue)
        {
            var offsetDeg = (radiusKm ?? DefaultVehicleRadiusKm) / GeoConstants.KmPerDegree;

            if (lat.HasValue)
            {
                query.Add(Invariant($"minLat={lat.Value - offsetDeg}"));
                query.Add(Invariant($"maxLat={lat.Value + offsetDeg}"));
            }

            if (lon.HasValue)
            {
                query.Add(Invariant($"minLon={lon.Value - offsetDeg}"));
                query.Add(Invariant($"maxLon={lon.Value + offsetDeg}"));
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/realtime/vehicles?{string.Join("&", query)}");
        return await SendWithAuthAsync<List<TransitVehicleResponse>>(request, ct);
    }

    public async Task<List<TransitAlertResponse>> GetAlertsAsync(string? stopOnestopId, string? routeOnestopId, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(stopOnestopId)) query.Add($"stopOnestopId={Uri.EscapeDataString(stopOnestopId)}");
        if (!string.IsNullOrEmpty(routeOnestopId)) query.Add($"routeOnestopId={Uri.EscapeDataString(routeOnestopId)}");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/realtime/alerts?{string.Join("&", query)}");
        return await SendWithAuthAsync<List<TransitAlertResponse>>(request, ct);
    }

    public async Task<List<TransitMobilityStationResponse>> GetMobilityStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        return await FetchAllPagesAsync<TransitMobilityStationResponse>(
            "/mobility/stations", BuildGeoQuery(lat, lon, radiusKm), ct);
    }

    private DateTime GetTokenExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return DateTime.UtcNow.AddHours(1);

            var payload = Encoding.UTF8.GetString(
                Convert.FromBase64String(Base64Helper.PadBase64(parts[1])));
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("exp", out var expProp) &&
                expProp.TryGetInt64(out var expSeconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime.AddMinutes(-5);
            }

            _logger.LogWarning("TransitInfoAPI access token carries no readable 'exp' claim; assuming a short lifetime.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            // Falling back silently would cache a 15-minute token for an hour and send expired
            // credentials upstream until the 401 retry kicks in.
            _logger.LogWarning(ex, "Could not read the expiry from the TransitInfoAPI access token.");
        }

        // Deliberately short: better to re-authenticate too often than to treat an unknown token as long-lived.
        return DateTime.UtcNow.AddMinutes(10);
    }

    private class LoginResult
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public UserInfo User { get; set; } = new();
    }

    private class UserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mirrors TransitInfoAPI's <c>Paginated&lt;T&gt;</c>, whose list property is <c>data</c>.
    /// <para>
    /// This declared <c>Items</c>, which matches nothing upstream sends — so it deserialised to an
    /// empty list every time and <see cref="MapManager"/>'s typed endpoints (<c>/api/map/stations</c>,
    /// <c>/routes</c>, <c>/mobility/stations</c>) silently returned <c>[]</c> for their entire
    /// existence. Nothing noticed because the map page was calling TransitInfoAPI directly.
    /// </para>
    /// </summary>
    private class PaginatedResponse<T>
    {
        public List<T> Data { get; set; } = [];
        public int Total { get; set; }
        public int Page { get; set; }
        public int PerPage { get; set; }
        public int TotalPages { get; set; }
    }
}

// Response DTOs matching TransitInfoAPI contracts
public class TransitStationResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string StationType { get; set; } = string.Empty;
    public string? PrimaryRouteType { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
}

public class TransitRouteResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string RouteType { get; set; } = string.Empty;
    public int? OperatorId { get; set; }
    public string? OperatorName { get; set; }
}

public class TransitVehicleResponse
{
    public string VehicleId { get; set; } = string.Empty;
    public string? FeedId { get; set; }
    public string? RouteId { get; set; }
    public string? TripId { get; set; }
    public string? RouteShortName { get; set; }
    public bool IsRealtime { get; set; }
    public string? BlockId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Bearing { get; set; }
    public DateTime? LastUpdated { get; set; }
}

public class TransitAlertResponse
{
    public int Id { get; set; }
    public string? HeaderText { get; set; }
    public string? DescriptionText { get; set; }
    public string? Url { get; set; }
    public string? Cause { get; set; }
    public string? Effect { get; set; }
    public DateTime? ActivePeriodStart { get; set; }
    public DateTime? ActivePeriodEnd { get; set; }
    public DateTime FetchedAt { get; set; }
    public string? AffectedStopIds { get; set; }
    public string? AffectedRouteIds { get; set; }
    public string? AffectedTripIds { get; set; }
    public string? AffectedAgencyIds { get; set; }
}

public class TransitMobilityStationResponse
{
    public int Id { get; set; }
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int AvailableVehicles { get; set; }
    public int? Capacity { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
    public string? CountryName { get; set; }
    public string? CountryCode { get; set; }
}
