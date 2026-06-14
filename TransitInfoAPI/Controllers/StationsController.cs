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
    public async Task<ActionResult<OperationResult<List<StationDto>>>> GetAll(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int? countryId,
        CancellationToken ct = default)
    {
        var result = await _stationService.GetAllAsync(lat, lon, radiusKm, countryId, ct);
        return Ok(OperationResult<List<StationDto>>.Ok(result));
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<OperationResult<StationDto>>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByGlobalIdAsync(globalId, ct);
        if (station is null) return NotFound(OperationResult<StationDto>.Fail("Station not found."));
        return Ok(OperationResult<StationDto>.Ok(station));
    }

    [HttpGet("{globalId}/operators")]
    public async Task<ActionResult<OperationResult<List<StationOperatorDto>>>> GetOperators(string globalId, CancellationToken ct = default)
    {
        var operators = await _stationService.GetOperatorsAsync(globalId, ct);
        return Ok(OperationResult<List<StationOperatorDto>>.Ok(operators));
    }

    [HttpGet("{globalId}/departures")]
    public async Task<ActionResult<OperationResult<List<DepartureDto>>>> GetDepartures(string globalId, CancellationToken ct = default)
    {
        var departures = await _stationService.GetDeparturesAsync(globalId, ct);
        return Ok(OperationResult<List<DepartureDto>>.Ok(departures));
    }
}
