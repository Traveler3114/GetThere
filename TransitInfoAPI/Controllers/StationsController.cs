using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class StationsController : ControllerBase
{
    private readonly StationService _stationService;

    public StationsController(StationService stationService) { _stationService = stationService; }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int? countryId,
        [FromQuery] string? format = null,
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var result = await _stationService.GetAllAsync(lat, lon, radiusKm, countryId, after, perPage, ct);

        if (format == "geojson")
        {
            var fc = GeoJsonGeometry.ToPointCollection(result,
                s => s.Latitude, s => s.Longitude,
                s => new Dictionary<string, object?>
                {
                    ["id"] = s.Id,
                    ["globalId"] = s.GlobalId,
                    ["onestopId"] = s.OnestopId,
                    ["name"] = s.Name,
                    ["stationType"] = s.StationType,
                    ["countryName"] = s.CountryName,
                    ["cityName"] = s.CityName
                });
            return Ok(fc);
        }

        var nextAfter = result.Count > 0 ? result.Max(r => r.Id) : after;
        var total = await _stationService.GetTotalCountAsync(ct);
        var nextUrl = result.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<StationDto>>.OkPaginated(result, nextAfter, total, nextUrl));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OperationResult<StationDto>>> GetById(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByGlobalIdAsync($"gt-{id}", ct);
        if (station is null) return NotFound(OperationResult<StationDto>.Fail("Station not found."));
        return Ok(OperationResult<StationDto>.Ok(station));
    }

    [HttpGet("by-onestop/{onestopId}")]
    public async Task<ActionResult<OperationResult<StationDto>>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByOnestopIdAsync(onestopId, ct);
        if (station is null) return NotFound(OperationResult<StationDto>.Fail("Station not found."));
        return Ok(OperationResult<StationDto>.Ok(station));
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<OperationResult<List<StationOperatorDto>>>> GetOperators(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByGlobalIdAsync($"gt-{id}", ct);
        if (station is null) return NotFound();
        var operators = await _stationService.GetOperatorsAsync(station.OnestopId, ct);
        return Ok(OperationResult<List<StationOperatorDto>>.Ok(operators));
    }

    [HttpGet("{id}/departures")]
    public async Task<ActionResult<OperationResult<List<DepartureDto>>>> GetDepartures(
        int id,
        [FromQuery] DateTime? from = null,
        [FromQuery] int count = 10,
        CancellationToken ct = default)
    {
        var departures = await _stationService.GetDeparturesAsync(id, from, count, ct);
        return Ok(OperationResult<List<DepartureDto>>.Ok(departures));
    }
}
