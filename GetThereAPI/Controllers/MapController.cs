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
    private readonly OperatorManager  _operators;
    private readonly MobilityManager  _mobility;
    private readonly AppDbContext     _db;

    public MapController(OperatorManager operators, MobilityManager mobility, AppDbContext db)
    {
        _operators = operators;
        _mobility  = mobility;
        _db        = db;
    }

    // GET /map/features
    [HttpGet("features")]
    public ActionResult<OperationResult<List<MapFeatureDto>>> GetFeatures()
    {
        var features = new List<MapFeatureDto>();

        // ── Transit stops ────────────────────────────────────────────────────
        foreach (var stop in _operators.GetAllStops())
        {
            features.Add(new MapFeatureDto
            {
                Type = "Stop",
                Lat  = stop.Lat,
                Lon  = stop.Lon,
                Data = JsonSerializer.SerializeToElement(stop)
            });
        }

        // ── Live vehicles ────────────────────────────────────────────────────
        foreach (var vehicle in _operators.GetAllVehicles())
        {
            features.Add(new MapFeatureDto
            {
                Type = "Vehicle",
                Lat  = vehicle.Lat,
                Lon  = vehicle.Lon,
                Data = JsonSerializer.SerializeToElement(vehicle)
            });
        }

        // ── Bike stations ────────────────────────────────────────────────────
        foreach (var station in _mobility.GetAllStations())
        {
            features.Add(new MapFeatureDto
            {
                Type = "BikeStation",
                Lat  = station.Lat,
                Lon  = station.Lon,
                Data = JsonSerializer.SerializeToElement(station)
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
