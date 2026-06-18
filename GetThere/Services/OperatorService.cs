using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.Services;

public class OperatorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public OperatorService(HttpClient http)
    {
        _http = http;
    }

    public async Task<OperationResult<List<TransportTypeResponse>>> GetTransportTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TransportTypeResponse>>>("api/map/operators/types", JsonOptions);
            return result ?? OperationResult<List<TransportTypeResponse>>.Fail("Could not load transport types");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TransportTypeResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapStationResponse>>> GetStopsAsync(int? countryId)
    {
        try
        {
            var url = countryId.HasValue ? $"api/map/stations?countryId={countryId.Value}" : "api/map/stations";
            var result = await _http.GetFromJsonAsync<OperationResult<List<MapStationResponse>>>(url, JsonOptions);
            return result ?? OperationResult<List<MapStationResponse>>.Fail("Could not load stops");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapStationResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapRouteResponse>>> GetRoutesAsync(int? countryId)
    {
        try
        {
            var url = "api/map/routes";
            var result = await _http.GetFromJsonAsync<OperationResult<List<MapRouteResponse>>>(url, JsonOptions);
            return result ?? OperationResult<List<MapRouteResponse>>.Fail("Could not load routes");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapRouteResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapMobilityStationResponse>>> GetBikeStationsAsync(int? countryId)
    {
        try
        {
            var url = "api/map/mobility/stations";
            var result = await _http.GetFromJsonAsync<OperationResult<List<MapMobilityStationResponse>>>(url, JsonOptions);
            return result ?? OperationResult<List<MapMobilityStationResponse>>.Fail("Could not load bike stations");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapMobilityStationResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapDepartureResponse>>> GetStationDeparturesAsync(string globalId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<MapDepartureResponse>>>($"api/map/stations/{globalId}/departures", JsonOptions);
            return result ?? OperationResult<List<MapDepartureResponse>>.Fail("Could not load departures");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapDepartureResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapOperatorResponse>>> GetStationOperatorsAsync(string globalId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<MapOperatorResponse>>>($"api/map/stations/{globalId}/operators", JsonOptions);
            return result ?? OperationResult<List<MapOperatorResponse>>.Fail("Could not load operators");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapOperatorResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapVehicleResponse>>> GetVehiclesAsync(double? lat = null, double? lon = null, double? radiusKm = null)
    {
        try
        {
            var parts = new List<string>();
            if (lat.HasValue) parts.Add($"lat={lat.Value}");
            if (lon.HasValue) parts.Add($"lon={lon.Value}");
            if (radiusKm.HasValue) parts.Add($"radiusKm={radiusKm.Value}");
            var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
            var result = await _http.GetFromJsonAsync<OperationResult<List<MapVehicleResponse>>>("api/map/realtime/vehicles" + qs, JsonOptions);
            return result ?? OperationResult<List<MapVehicleResponse>>.Fail("Could not load vehicles");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapVehicleResponse>>.Fail(ex.Message);
        }
    }
}

public class TransportTypeResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconFile { get; set; } = string.Empty;
}
