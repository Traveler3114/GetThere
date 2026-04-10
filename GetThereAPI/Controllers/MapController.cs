using System.Text.Json;
using GetThereAPI.Data;
using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class MapController : ControllerBase
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TransitlandManager _transitland;
    private readonly AppDbContext _db;
    private readonly ILogger<MapController> _logger;

    public MapController(
        TransitlandManager transitland,
        AppDbContext db,
        ILogger<MapController> logger)
    {
        _transitland = transitland;
        _db = db;
        _logger = logger;
    }

    [HttpGet("features")]
    public async Task<ActionResult<OperationResult<List<MapFeatureDto>>>> GetFeatures(
        [FromQuery] int? countryId = null,
        CancellationToken cancellationToken = default)
    {
        var features = new List<MapFeatureDto>();
        var countryName = await ResolveCountryNameAsync(countryId, cancellationToken);

        _logger.LogInformation("[DEBUG] GetFeatures called: countryId={CountryId} resolvedName={CountryName}",
            countryId, countryName ?? "(null)");

        if (countryId.HasValue && countryName is null)
        {
            _logger.LogWarning("[DEBUG] countryId={CountryId} not found in DB — returning empty", countryId);
            return Ok(OperationResult<List<MapFeatureDto>>.Ok([]));
        }

        var stops = await _transitland.GetStopsAsync(countryName, cancellationToken);
        _logger.LogInformation("[DEBUG] GetStopsAsync returned {Count} stops", stops.Count);

        foreach (var stop in stops)
        {
            features.Add(new MapFeatureDto
            {
                Type = "Stop",
                Lat = stop.Lat,
                Lon = stop.Lon,
                Data = JsonSerializer.SerializeToElement(stop, CamelCaseJson)
            });
        }

        _logger.LogInformation("[DEBUG] Returning {Count} total features", features.Count);
        return Ok(OperationResult<List<MapFeatureDto>>.Ok(features));
    }

    [HttpGet("bike-stations")]
    public ActionResult<OperationResult<List<BikeStationDto>>> GetBikeStations()
        => Ok(OperationResult<List<BikeStationDto>>.Ok([]));

    [HttpGet("tiles-config")]
    public ActionResult<OperationResult<MapTilesConfigDto>> GetTilesConfig()
    {
        return Ok(OperationResult<MapTilesConfigDto>.Ok(new MapTilesConfigDto
        {
            TilesBaseUrl = _transitland.GetTilesBaseUrl(),
            ApiKey = _transitland.GetApiKey()
        }));
    }

    private async Task<string?> ResolveCountryNameAsync(int? countryId, CancellationToken cancellationToken = default)
    {
        if (!countryId.HasValue)
            return null;

        return await _db.Countries
            .Where(c => c.Id == countryId.Value)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
