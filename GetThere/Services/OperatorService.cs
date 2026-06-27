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
            var response = await _http.GetAsync("api/map/operators/types");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<TransportTypeResponse>>(JsonOptions);
                return OperationResult<List<TransportTypeResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<TransportTypeResponse>>.Fail(problem ?? "Could not load transport types");
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
            var response = await _http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<MapStationResponse>>(JsonOptions);
                return OperationResult<List<MapStationResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<MapStationResponse>>.Fail(problem ?? "Could not load stops");
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
            var response = await _http.GetAsync("api/map/routes");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<MapRouteResponse>>(JsonOptions);
                return OperationResult<List<MapRouteResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<MapRouteResponse>>.Fail(problem ?? "Could not load routes");
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
            var response = await _http.GetAsync("api/map/mobility/stations");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<MapMobilityStationResponse>>(JsonOptions);
                return OperationResult<List<MapMobilityStationResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<MapMobilityStationResponse>>.Fail(problem ?? "Could not load bike stations");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapMobilityStationResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapDepartureResponse>>> GetStationDeparturesAsync(string onestopId)
    {
        try
        {
            var response = await _http.GetAsync($"api/map/stations/{onestopId}/departures");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<MapDepartureResponse>>(JsonOptions);
                return OperationResult<List<MapDepartureResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<MapDepartureResponse>>.Fail(problem ?? "Could not load departures");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapDepartureResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<MapOperatorResponse>>> GetStationOperatorsAsync(string onestopId)
    {
        try
        {
            var response = await _http.GetAsync($"api/map/stations/{onestopId}/operators");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<MapOperatorResponse>>(JsonOptions);
                return OperationResult<List<MapOperatorResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<MapOperatorResponse>>.Fail(problem ?? "Could not load operators");
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
            var response = await _http.GetAsync("api/map/realtime/vehicles" + qs);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<MapVehicleResponse>>(JsonOptions);
                return OperationResult<List<MapVehicleResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<MapVehicleResponse>>.Fail(problem ?? "Could not load vehicles");
        }
        catch (Exception ex)
        {
            return OperationResult<List<MapVehicleResponse>>.Fail(ex.Message);
        }
    }

    private static async Task<string?> TryReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (doc.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch { }
        return null;
    }
}


