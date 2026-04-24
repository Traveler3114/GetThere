using GetThereShared.Common;
using GetThereShared.Dtos;
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

    public async Task<OperationResult<List<OperatorDto>>> GetOperatorsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<OperatorDto>>>("operator");
            return result ?? OperationResult<List<OperatorDto>>.Fail("No response received from API when loading operators.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<OperatorDto>>.Fail($"Could not load operators: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<StopDto>>> GetStopsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/stops?countryId={countryId.Value}"
                : "operator/stops";
            var result = await _http.GetFromJsonAsync<OperationResult<List<StopDto>>>(url);
            return result ?? OperationResult<List<StopDto>>.Fail("No response received from API when loading stops.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<StopDto>>.Fail($"Could not load stops: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<RouteDto>>> GetRoutesAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/routes?countryId={countryId.Value}"
                : "operator/routes";
            var result = await _http.GetFromJsonAsync<OperationResult<List<RouteDto>>>(url);
            return result ?? OperationResult<List<RouteDto>>.Fail("No response received from API when loading routes.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<RouteDto>>.Fail($"Could not load routes: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<BikeStationDto>>> GetBikeStationsAsync(int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"map/bike-stations?countryId={countryId.Value}"
                : "map/bike-stations";
            var result = await _http.GetFromJsonAsync<OperationResult<List<BikeStationDto>>>(url);
            return result ?? OperationResult<List<BikeStationDto>>.Fail("No response received from API when loading bike stations.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<BikeStationDto>>.Fail($"Could not load bike stations: {ex.Message}");
        }
    }

    public async Task<OperationResult<List<TransportTypeDto>>> GetTransportTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TransportTypeDto>>>(
                "operator/transport-types");
            return result ?? OperationResult<List<TransportTypeDto>>.Fail("No response received from API when loading transport types.");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TransportTypeDto>>.Fail($"Could not load transport types: {ex.Message}");
        }
    }

    public async Task<OperationResult<StopScheduleDto>> GetStopScheduleAsync(string stopId, int? countryId = null)
    {
        try
        {
            var url = countryId.HasValue
                ? $"operator/stops/{Uri.EscapeDataString(stopId)}/schedule?countryId={countryId.Value}"
                : $"operator/stops/{Uri.EscapeDataString(stopId)}/schedule";
            var result = await _http.GetFromJsonAsync<OperationResult<StopScheduleDto>>(url);
            return result ?? OperationResult<StopScheduleDto>.Fail("No response received from API when loading stop schedule.");
        }
        catch (Exception ex)
        {
            return OperationResult<StopScheduleDto>.Fail($"Could not load stop schedule: {ex.Message}");
        }
    }
}
