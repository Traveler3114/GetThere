using GetThereShared.Common;
using GetThereShared.Dtos;
using System.Net.Http.Json;
using System.Diagnostics;

namespace GetThere.Services;

/// <summary>
/// Makes all transit-related HTTP calls to the GetThere API.
/// All methods return null on failure and log the error —
/// the UI handles the null case gracefully.
/// </summary>
public class OperatorService
{
    private readonly HttpClient _http;

    public OperatorService(HttpClient http)
    {
        _http = http;
    }

    // ── API base URL ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the base URL of the API (e.g. "https://localhost:7230").
    /// Used by MapPage to inject window._API_BASE into the WebView so the
    /// JS icon loader can fetch map icons via GET /operator/images/*.png.
    /// </summary>
    public string GetApiBaseUrl()
        => _http.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    // ── Operators ─────────────────────────────────────────────────────────

    /// <summary>Returns all available transit operators.</summary>
    public async Task<List<OperatorDto>?> GetOperatorsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<OperatorDto>>>(
                "operator");
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetOperators failed: {ex.Message}");
            return null;
        }
    }

    // ── Map data ──────────────────────────────────────────────────────────

    public async Task<List<MapFeatureDto>?> GetMapFeaturesAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"map/features?countryId={countryId.Value}"
                : "map/features";
            var result = await _http.GetFromJsonAsync<OperationResult<List<MapFeatureDto>>>(url);
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetMapFeatures failed: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetMapStyleUrlAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<MapStyleConfigDto>>("map/style-url");
            return result?.Data?.StyleUrl;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetMapStyleUrl failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns all stops for operators in the given country, or all stops when countryId is null.
    /// Called once on app startup — result should be cached locally.
    /// </summary>
    public async Task<List<StopDto>?> GetStopsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/stops?countryId={countryId.Value}"
                : "operator/stops";
            var result = await _http.GetFromJsonAsync<OperationResult<List<StopDto>>>(url);
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetStops failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns all routes for the given country, or all routes when countryId is null.
    /// Called once on app startup — result should be cached locally.
    /// </summary>
    public async Task<List<RouteDto>?> GetRoutesAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/routes?countryId={countryId.Value}"
                : "operator/routes";
            var result = await _http.GetFromJsonAsync<OperationResult<List<RouteDto>>>(url);
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetRoutes failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns all bike-share stations for the given country, or all stations when countryId is null.
    /// Called once on app startup; stations change slowly.
    /// </summary>
    public async Task<List<BikeStationDto>?> GetBikeStationsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"map/bike-stations?countryId={countryId.Value}"
                : "map/bike-stations";
            var result = await _http.GetFromJsonAsync<OperationResult<List<BikeStationDto>>>(url);
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetBikeStations failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns all live vehicle positions for the given country, or all vehicles when countryId is null.
    /// Called every 10 seconds to keep the map up to date.
    /// </summary>
    public async Task<List<VehicleDto>?> GetVehiclesAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/vehicles?countryId={countryId.Value}"
                : "operator/vehicles";
            var result = await _http.GetFromJsonAsync<OperationResult<List<VehicleDto>>>(url);
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetVehicles failed: {ex.Message}");
            return null;
        }
    }

    // ── Transport types ───────────────────────────────────────────────────

    /// <summary>
    /// Returns transport types that have icons available on the server.
    /// Used by the map to build icon map and layer expressions dynamically.
    /// Called once on app startup.
    /// </summary>
    public async Task<List<TransportTypeDto>?> GetTransportTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TransportTypeDto>>>(
                "operator/transport-types");
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetTransportTypes failed: {ex.Message}");
            return null;
        }
    }

    // ── Schedule ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns today's departures for a stop with realtime delays merged in.
    /// Called when a user taps a stop on the map.
    /// </summary>
    public async Task<StopScheduleDto?> GetStopScheduleAsync(string stopId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<StopScheduleDto>>(
                $"operator/stops/{Uri.EscapeDataString(stopId)}/schedule");
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetStopSchedule({stopId}) failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns the full stop sequence for a trip with realtime delays.
    /// Called when a user taps a vehicle on the map.
    /// </summary>
    public async Task<TripDetailDto?> GetTripDetailAsync(string tripId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<TripDetailDto>>(
                $"operator/trips/{Uri.EscapeDataString(tripId)}");
            return result?.Data;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OperatorService] GetTripDetail({tripId}) failed: {ex.Message}");
            return null;
        }
    }
}
