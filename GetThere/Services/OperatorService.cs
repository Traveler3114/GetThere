using System.Net.Http.Json;

using GetThere.Localization;
using GetThereShared.Common;
using GetThereShared.Contracts;

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
            return result ?? OperationResult<List<OperatorResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "operators"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<OperatorResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadOperators"] + ex.Message);
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
            return result ?? OperationResult<List<StopResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "stops"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<StopResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadStops"] + ex.Message);
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
            return result ?? OperationResult<List<RouteResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "routes"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<RouteResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadRoutes"] + ex.Message);
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
            return result ?? OperationResult<List<BikeStationResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "bike stations"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<BikeStationResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadBikeStations"] + ex.Message);
        }
    }

    public async Task<OperationResult<List<TransportTypeResponse>>> GetTransportTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TransportTypeResponse>>>(
                "operator/transport-types");
            return result ?? OperationResult<List<TransportTypeResponse>>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "transport types"));
        }
        catch (Exception ex)
        {
            return OperationResult<List<TransportTypeResponse>>.Fail(LocalizationService.Instance["Error_CouldNotLoadTransportTypes"] + ex.Message);
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
            return result ?? OperationResult<StopScheduleResponse>.Fail(string.Format(LocalizationService.Instance["Shop_NoResponse"], "stop schedule"));
        }
        catch (Exception ex)
        {
            return OperationResult<StopScheduleResponse>.Fail(LocalizationService.Instance["Error_CouldNotLoadSchedule"] + ex.Message);
        }
    }
}
