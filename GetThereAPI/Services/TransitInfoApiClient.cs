using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GetThereShared.Common;

namespace GetThereAPI.Services;

public class TransitInfoApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string ClientId { get; set; } = "getthere-api@transit.local";
    public string ClientSecret { get; set; } = "";
}

public class TransitInfoApiClient
{
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

            var response = await _httpClient.PostAsJsonAsync("/auth/login", loginRequest, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var loginResult = JsonSerializer.Deserialize<LoginResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _cachedAccessToken = loginResult?.AccessToken ?? throw new Exception("Failed to get access token from TransitInfoAPI");
            _tokenExpiry = GetTokenExpiry(_cachedAccessToken);
            _logger.LogInformation("Obtained new access token from TransitInfoAPI, expires at {Expiry}", _tokenExpiry);

            return _cachedAccessToken;
        }
        finally
        {
            _tokenSemaphore.Release();
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
            _cachedAccessToken = null;
            token = await GetAccessTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await _httpClient.SendAsync(request, ct);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public async Task<List<TransitStationResponse>> GetStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (lat.HasValue) query.Add($"lat={lat.Value}");
        if (lon.HasValue) query.Add($"lon={lon.Value}");
        if (radiusKm.HasValue) query.Add($"radiusKm={radiusKm.Value}");
        query.Add("perPage=500");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/stations?{string.Join("&", query)}");
        var result = await SendWithAuthAsync<PaginatedResponse<TransitStationResponse>>(request, ct);
        return result.Items;
    }

    public async Task<List<TransitRouteResponse>> GetRoutesAsync(int? operatorId, string? routeType, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (operatorId.HasValue) query.Add($"operatorId={operatorId.Value}");
        if (!string.IsNullOrEmpty(routeType)) query.Add($"routeType={routeType}");
        query.Add("perPage=500");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/routes?{string.Join("&", query)}");
        var result = await SendWithAuthAsync<PaginatedResponse<TransitRouteResponse>>(request, ct);
        return result.Items;
    }

    public async Task<List<TransitVehicleResponse>> GetVehiclesAsync(string? feedId, double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(feedId)) query.Add($"feedId={feedId}");
        if (lat.HasValue) query.Add($"minLat={lat.Value}");
        if (lon.HasValue) query.Add($"minLon={lon.Value}");
        if (lat.HasValue) query.Add($"maxLat={lat.Value + (radiusKm ?? 50) / 111.0}");
        if (lon.HasValue) query.Add($"maxLon={lon.Value + (radiusKm ?? 50) / 111.0}");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/realtime/vehicles?{string.Join("&", query)}");
        return await SendWithAuthAsync<List<TransitVehicleResponse>>(request, ct);
    }

    public async Task<List<TransitAlertResponse>> GetAlertsAsync(string? stopOnestopId, string? routeOnestopId, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(stopOnestopId)) query.Add($"stopOnestopId={stopOnestopId}");
        if (!string.IsNullOrEmpty(routeOnestopId)) query.Add($"routeOnestopId={routeOnestopId}");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/realtime/alerts?{string.Join("&", query)}");
        return await SendWithAuthAsync<List<TransitAlertResponse>>(request, ct);
    }

    public async Task<List<TransitMobilityStationResponse>> GetMobilityStationsAsync(double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (lat.HasValue) query.Add($"lat={lat.Value}");
        if (lon.HasValue) query.Add($"lon={lon.Value}");
        if (radiusKm.HasValue) query.Add($"radiusKm={radiusKm.Value}");
        query.Add("perPage=500");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/mobility/stations?{string.Join("&", query)}");
        var result = await SendWithAuthAsync<PaginatedResponse<TransitMobilityStationResponse>>(request, ct);
        return result.Items;
    }

    private static DateTime GetTokenExpiry(string token)
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
        }
        catch { }

        return DateTime.UtcNow.AddHours(1);
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

    private class PaginatedResponse<T>
    {
        public List<T> Items { get; set; } = [];
        public int Total { get; set; }
        public int Page { get; set; }
        public int PerPage { get; set; }
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