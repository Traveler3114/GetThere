using GetThereShared.Common;
using GetThereShared.Contracts;
using System.Net.Http.Json;

namespace GetThere.Services;

public class OperatorService
{
    private readonly HttpClient _http;

    public OperatorService(HttpClient http)
    {
        _http = http;
    }

    public string GetApiBaseUrl()
        => _http.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public async Task<OperationResult<List<OperatorResponse>>> GetOperatorsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<OperatorResponse>>>("operator");
            return result ?? OperationResult<List<OperatorResponse>>.Fail("No response received from API when loading operators.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<OperatorResponse>>.Fail($"Could not load operators: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<StopResponse>>> GetStopsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/stops?countryId={countryId.Value}"
                : "operator/stops";
            var result = await _http.GetFromJsonAsync<OperationResult<List<StopResponse>>>(url);
            return result ?? OperationResult<List<StopResponse>>.Fail("No response received from API when loading stops.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<StopResponse>>.Fail($"Could not load stops: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<RouteResponse>>> GetRoutesAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/routes?countryId={countryId.Value}"
                : "operator/routes";
            var result = await _http.GetFromJsonAsync<OperationResult<List<RouteResponse>>>(url);
            return result ?? OperationResult<List<RouteResponse>>.Fail("No response received from API when loading routes.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<RouteResponse>>.Fail($"Could not load routes: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<BikeStationResponse>>> GetBikeStationsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"map/bike-stations?countryId={countryId.Value}"
                : "map/bike-stations";
            var result = await _http.GetFromJsonAsync<OperationResult<List<BikeStationResponse>>>(url);
            return result ?? OperationResult<List<BikeStationResponse>>.Fail("No response received from API when loading bike stations.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<BikeStationResponse>>.Fail($"Could not load bike stations: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<TransportTypeResponse>>> GetTransportTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TransportTypeResponse>>>(
                "operator/transport-types");
            return result ?? OperationResult<List<TransportTypeResponse>>.Fail("No response received from API when loading transport types.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TransportTypeResponse>>.Fail($"Could not load transport types: {ex.Message}");
        }
    }

    public async Task<OperationResult<StopScheduleResponse>> GetStopScheduleAsync(string stopId, int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/stops/{Uri.EscapeDataString(stopId)}/schedule?countryId={countryId.Value}"
                : $"operator/stops/{Uri.EscapeDataString(stopId)}/schedule";
            var result = await _http.GetFromJsonAsync<OperationResult<StopScheduleResponse>>(url);
            return result ?? OperationResult<StopScheduleResponse>.Fail("No response received from API when loading stop schedule.");
        }
        catch (Exception ex)
        {
            return OperationResult<StopScheduleResponse>.Fail($"Could not load stop schedule: {ex.Message}");
        }
    }
}
