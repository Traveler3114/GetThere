using System.Text.Json;
using System.Text.RegularExpressions;

using GetThereAPI.Exceptions;
using GetThereAPI.Services;

using GetThereShared.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GetThereAPI.Managers;

public class MapManager
{
    private readonly TransitInfoApiClient _transitClient;
    private readonly ILogger<MapManager> _logger;

    public MapManager(TransitInfoApiClient transitClient, ILogger<MapManager> logger)
    {
        _transitClient = transitClient;
        _logger = logger;
    }

    public async Task<List<MapStationResponse>> GetStationsAsync(
        double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var stations = await _transitClient.GetStationsAsync(lat, lon, radiusKm, ct);
        return stations.Select(s => new MapStationResponse
        {
            Id = s.Id,
            OnestopId = s.OnestopId,
            Name = s.Name,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            StationType = s.StationType
        }).ToList();
    }

    public async Task<List<MapRouteResponse>> GetRoutesAsync(
        int? operatorId, string? routeType, CancellationToken ct = default)
    {
        var routes = await _transitClient.GetRoutesAsync(operatorId, routeType, ct);
        return routes.Select(r => new MapRouteResponse
        {
            Id = r.Id,
            OnestopId = r.OnestopId,
            Name = r.Name,
            RouteType = r.RouteType,
            OperatorName = r.OperatorName ?? string.Empty
        }).ToList();
    }

    public async Task<List<MapMobilityStationResponse>> GetMobilityStationsAsync(
        double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var stations = await _transitClient.GetMobilityStationsAsync(lat, lon, radiusKm, ct);
        return stations.Select(s => new MapMobilityStationResponse
        {
            StationId = s.StationId,
            Name = s.Name,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            AvailableVehicles = s.AvailableVehicles,
            Capacity = s.Capacity ?? 0,
            ProviderName = s.ProviderName
        }).ToList();
    }

    public async Task<List<MapVehicleResponse>> GetVehiclesAsync(
        string? feedId, double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var vehicles = await _transitClient.GetVehiclesAsync(feedId, lat, lon, radiusKm, ct);
        return vehicles.Select(v => new MapVehicleResponse
        {
            VehicleId = v.VehicleId,
            RouteId = v.RouteId,
            TripId = v.TripId,
            RouteShortName = v.RouteShortName,
            IsRealtime = v.IsRealtime,
            BlockId = v.BlockId,
            Latitude = v.Latitude,
            Longitude = v.Longitude,
            Bearing = v.Bearing,
            LastUpdated = v.LastUpdated
        }).ToList();
    }

    private static readonly JsonSerializerOptions UpstreamJson = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<MapDepartureResponse>> GetDeparturesAsync(string stationId, CancellationToken ct = default)
    {
        var (body, _) = await _transitClient.GetRawAsync($"/stations/{Uri.EscapeDataString(stationId)}/departures", ct);
        return JsonSerializer.Deserialize<List<MapDepartureResponse>>(body, UpstreamJson) ?? [];
    }

    public async Task<List<MapOperatorResponse>> GetStationOperatorsAsync(string stationId, CancellationToken ct = default)
    {
        var (body, _) = await _transitClient.GetRawAsync($"/stations/{Uri.EscapeDataString(stationId)}/operators", ct);
        return JsonSerializer.Deserialize<List<MapOperatorResponse>>(body, UpstreamJson) ?? [];
    }

    public async Task<JsonElement> GetTransportTypesAsync(CancellationToken ct = default)
    {
        var (body, _) = await _transitClient.GetRawAsync("/map/transport-types", ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Upstream paths the map page is allowed to reach through this API.
    /// <para>
    /// An allowlist, not a filter: forwarding an arbitrary path would turn the proxy into an open
    /// gateway to TransitInfoAPI carrying the service account's credentials, letting any user with
    /// <c>map.view</c> reach admin endpoints there.
    /// </para>
    /// </summary>
    private static readonly Regex[] AllowedUpstreamPaths =
    [
        new(@"^stations$", RegexOptions.Compiled),
        new(@"^routes$", RegexOptions.Compiled),
        new(@"^mobility/stations$", RegexOptions.Compiled),
        new(@"^realtime/vehicles$", RegexOptions.Compiled),
        new(@"^realtime/alerts$", RegexOptions.Compiled),
        new(@"^stations/\d+/departures$", RegexOptions.Compiled),
        new(@"^stations/\d+/operators$", RegexOptions.Compiled),
        new(@"^map/transport-types$", RegexOptions.Compiled)
    ];

    public static bool IsAllowedUpstreamPath(string path) =>
        !string.IsNullOrWhiteSpace(path) && AllowedUpstreamPaths.Any(p => p.IsMatch(path));

    /// <summary>
    /// Forwards a whitelisted read to TransitInfoAPI verbatim. The map page consumes upstream shapes
    /// (GeoJSON feature collections in particular) directly.
    /// </summary>
    public async Task<(string Body, string ContentType)> GetUpstreamAsync(
        string path, string? queryString, CancellationToken ct = default)
    {
        if (!IsAllowedUpstreamPath(path))
        {
            _logger.LogWarning("Blocked map proxy request for non-whitelisted upstream path {Path}", path);
            throw new AppException("Unknown map resource.", 404, "UNKNOWN_MAP_RESOURCE");
        }

        return await _transitClient.GetRawAsync($"/{path}{queryString}", ct);
    }
}
