using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThereAPI.Controllers;

/// <summary>
/// Unified map endpoint.
///
/// GET /map/features  → all map features (stops, bike stations …)
///                      each wrapped in a <see cref="MapFeatureResponse"/> envelope.
///
/// The client only ever calls this one endpoint, reads the Type discriminator,
/// and renders the appropriate marker.  Adding new mobility modes (scooters,
/// ferries, …) means adding items to the list — no client-side endpoint changes.
/// </summary>
[ApiController]
[Route("[controller]")]
public class MapController : ControllerBase
{
    private readonly OperatorManager _operators;
    private readonly TransitDataService _transitData;

    public MapController(OperatorManager operators, TransitDataService transitData)
    {
        _operators = operators;
        _transitData = transitData;
    }

    [HttpGet("features")]
    public async Task<ActionResult<OperationResult<List<MapFeatureResponse>>>> GetFeatures(
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        List<MapFeatureResponse> features = [];

        foreach (var stop in await _transitData.GetAllStopsAsync(countryId, ct))
        {
            features.Add(new MapFeatureResponse
            {
                Type = MapFeatureType.Stop,
                Lat = stop.Lat,
                Lon = stop.Lon,
                Data = JsonSerializer.SerializeToElement(stop)
            });
        }

        foreach (var station in await _operators.GetBikeStationsAsync(countryId, ct))
        {
            features.Add(new MapFeatureResponse
            {
                Type = MapFeatureType.BikeStation,
                Lat = station.Lat,
                Lon = station.Lon,
                Data = JsonSerializer.SerializeToElement(station)
            });
        }

        return Ok(OperationResult<List<MapFeatureResponse>>.Ok(features));
    }

    [HttpGet("bike-stations")]
    public async Task<ActionResult<OperationResult<List<BikeStationResponse>>>> GetBikeStations(
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        var stations = await _operators.GetBikeStationsAsync(countryId, ct);
        return Ok(OperationResult<List<BikeStationResponse>>.Ok(stations));
    }
}
