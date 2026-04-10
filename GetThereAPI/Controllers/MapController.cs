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

    public MapController(
        TransitlandManager transitland,
        AppDbContext db)
    {
        _transitland = transitland;
        _db = db;
    }

    [HttpGet("features")]
    public async Task<ActionResult<OperationResult<List<MapFeatureDto>>>> GetFeatures(
        [FromQuery] int? countryId = null,
        CancellationToken cancellationToken = default)
    {
        var features = new List<MapFeatureDto>();
        var countryName = await ResolveCountryNameAsync(countryId, cancellationToken);

        if (countryId.HasValue && countryName is null)
            return Ok(OperationResult<List<MapFeatureDto>>.Ok([]));

        var stops = await _transitland.GetStopsAsync(countryName, cancellationToken);
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

        return Ok(OperationResult<List<MapFeatureDto>>.Ok(features));
    }

    [HttpGet("bike-stations")]
    public ActionResult<OperationResult<List<BikeStationDto>>> GetBikeStations()
        => Ok(OperationResult<List<BikeStationDto>>.Ok([]));

    [HttpGet("style-url")]
    public ActionResult<OperationResult<MapStyleConfigDto>> GetStyleUrl()
    {
        return Ok(OperationResult<MapStyleConfigDto>.Ok(new MapStyleConfigDto
        {
            StyleUrl = _transitland.GetTilesStyleUrl()
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
