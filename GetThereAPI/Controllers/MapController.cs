using System.Text.Json;
using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

/// <summary>
/// Unified map endpoint.
///
/// GET /map/features  → all map features (stops, bike stations …)
///                      each wrapped in a <see cref="MapFeatureDto"/> envelope.
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
    public async Task<ActionResult<OperationResult<List<MapFeatureDto>>>> GetFeatures(
        [FromQuery] int? countryId = null)
    {
        var features = new List<MapFeatureDto>();

        foreach (var stop in await _transitData.GetAllStopsAsync(countryId))
        {
            features.Add(new MapFeatureDto
            {
                Type = "Stop",
                Lat = stop.Lat,
                Lon = stop.Lon,
                Data = JsonSerializer.SerializeToElement(stop)
            });
        }

        foreach (var station in await _operators.GetBikeStationsAsync(countryId))
        {
            features.Add(new MapFeatureDto
            {
                Type = "BikeStation",
                Lat = station.Lat,
                Lon = station.Lon,
                Data = JsonSerializer.SerializeToElement(station)
            });
        }

        return Ok(OperationResult<List<MapFeatureDto>>.Ok(features));
    }

    [HttpGet("bike-stations")]
    public async Task<ActionResult<OperationResult<List<BikeStationDto>>>> GetBikeStations(
        [FromQuery] int? countryId = null)
    {
        var stations = await _operators.GetBikeStationsAsync(countryId);
        return Ok(OperationResult<List<BikeStationDto>>.Ok(stations));
    }
}
