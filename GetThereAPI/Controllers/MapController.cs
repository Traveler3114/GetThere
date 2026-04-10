using System.Text.Json;
using GetThereAPI.Data;
using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Controllers;

/// <summary>
/// Unified map endpoint.
///
/// GET /map/features  → all map features (stops, vehicles, bike stations …)
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
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly OperatorManager  _operators;
    private readonly MobilityManager  _mobility;
    private readonly TransitlandManager _transitland;
    private readonly AppDbContext     _db;

    public MapController(
        OperatorManager operators,
        MobilityManager mobility,
        TransitlandManager transitland,
        AppDbContext db)
    {
        _operators = operators;
        _mobility  = mobility;
        _transitland = transitland;
        _db        = db;
    }

    // GET /map/features
    [HttpGet("features")]
    public async Task<ActionResult<OperationResult<List<MapFeatureDto>>>> GetFeatures(
        [FromQuery] int? countryId = null,
        CancellationToken cancellationToken = default)
    {
        var features = new List<MapFeatureDto>();
        string? countryName = null;

        if (countryId.HasValue)
        {
            countryName = await _db.Countries
                .Where(c => c.Id == countryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (countryName is null)
                return Ok(OperationResult<List<MapFeatureDto>>.Ok([]));
        }

        // ── Transit stops ────────────────────────────────────────────────────
        var transitlandStops = await _transitland.GetStopsAsync(countryName, cancellationToken);
        foreach (var stop in transitlandStops)
        {
            features.Add(new MapFeatureDto
            {
                Type = "Stop",
                Lat  = stop.Lat,
                Lon  = stop.Lon,
                Data = JsonSerializer.SerializeToElement(stop, CamelCaseJson)
            });
        }

        // ── Live vehicles ────────────────────────────────────────────────────
        foreach (var vehicle in _operators.GetAllVehicles(countryId))
        {
            features.Add(new MapFeatureDto
            {
                Type = "Vehicle",
                Lat  = vehicle.Lat,
                Lon  = vehicle.Lon,
                Data = JsonSerializer.SerializeToElement(vehicle, CamelCaseJson)
            });
        }

        // ── Bike stations ────────────────────────────────────────────────────
        foreach (var station in _mobility.GetAllStations(countryName))
        {
            features.Add(new MapFeatureDto
            {
                Type = "BikeStation",
                Lat  = station.Lat,
                Lon  = station.Lon,
                Data = JsonSerializer.SerializeToElement(station, CamelCaseJson)
            });
        }

        return Ok(OperationResult<List<MapFeatureDto>>.Ok(features));
    }

    // GET /map/bike-stations
    // GET /map/bike-stations?countryId=1
    [HttpGet("bike-stations")]
    public async Task<ActionResult<OperationResult<List<BikeStationDto>>>> GetBikeStations(
        [FromQuery] int? countryId = null)
    {
        string? countryName = null;
        if (countryId.HasValue)
        {
            countryName = await _db.Countries
                .Where(c => c.Id == countryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            if (countryName is null)
                return Ok(OperationResult<List<BikeStationDto>>.Ok([]));
        }

        return Ok(OperationResult<List<BikeStationDto>>.Ok(_mobility.GetAllStations(countryName)));
    }
}
