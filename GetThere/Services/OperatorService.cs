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
    private readonly string _apiBase;

    public OperatorService(IHttpClientFactory clientFactory)
    {
        _http = clientFactory.CreateClient("MapData");
        _apiBase = "https://localhost:5000";
    }

    public string GetApiBaseUrl() => _apiBase;

    public async Task<OperationResult<List<TransportTypeResponse>>> GetTransportTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OperationResult<List<TransportTypeResponse>>>("operators/types", JsonOptions);
            return result ?? OperationResult<List<TransportTypeResponse>>.Fail("Could not load transport types");
        }
        catch (Exception ex)
        {
            return OperationResult<List<TransportTypeResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<StopResponse>>> GetStopsAsync(int? countryId)
    {
        try
        {
            var url = countryId.HasValue ? $"stations?countryId={countryId.Value}" : "stations";
            var result = await _http.GetFromJsonAsync<OperationResult<List<StopResponse>>>(url, JsonOptions);
            return result ?? OperationResult<List<StopResponse>>.Fail("Could not load stops");
        }
        catch (Exception ex)
        {
            return OperationResult<List<StopResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<RouteResponse>>> GetRoutesAsync(int? countryId)
    {
        try
        {
            var url = countryId.HasValue ? $"routes?countryId={countryId.Value}" : "routes";
            var result = await _http.GetFromJsonAsync<OperationResult<List<RouteResponse>>>(url, JsonOptions);
            return result ?? OperationResult<List<RouteResponse>>.Fail("Could not load routes");
        }
        catch (Exception ex)
        {
            return OperationResult<List<RouteResponse>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<List<BikeStationResponse>>> GetBikeStationsAsync(int? countryId)
    {
        return OperationResult<List<BikeStationResponse>>.Ok([]);
    }

    public async Task<OperationResult<StopScheduleResponse>> GetStopScheduleAsync(string stopId, int? countryId)
    {
        return OperationResult<StopScheduleResponse>.Ok(new StopScheduleResponse());
    }
}

public class TransportTypeResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconFile { get; set; } = string.Empty;
}

public class StopResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? GlobalId { get; set; }
}

public class RouteResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GlobalId { get; set; }
}

public class BikeStationResponse
{
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class StopScheduleResponse { }
