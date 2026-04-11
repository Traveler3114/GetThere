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
    private readonly TransitlandManager _transitland;
    private readonly AppDbContext _db;
    private readonly ILogger<MapController> _logger;

    public MapController(TransitlandManager transitland, AppDbContext db, ILogger<MapController> logger)
    {
        _transitland = transitland;
        _db = db;
        _logger = logger;
    }

    // GET /map/stops?bbox=minLon,minLat,maxLon,maxLat
    [HttpGet("stops")]
    public async Task<ActionResult<OperationResult<List<StopDto>>>> GetStops(
        [FromQuery] string bbox,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bbox))
            return BadRequest(OperationResult<List<StopDto>>.Fail("bbox is required"));

        var stops = await _transitland.GetStopsByBboxAsync(bbox, cancellationToken);
        return Ok(OperationResult<List<StopDto>>.Ok(stops));
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
}