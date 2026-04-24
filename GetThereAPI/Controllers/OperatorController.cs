using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

/// <summary>
/// All transit-related endpoints.
///
/// GET /operator                         → list of all operators (optional ?countryId=)
/// GET /operator/ticketable              → operators available for ticket purchase (optional ?countryId=)
/// GET /operator/stops                   → all stops across all operators (optional ?countryId=)
/// GET /operator/routes                  → all routes across all operators (optional ?countryId=)
/// GET /operator/stops/{id}/schedule     → departures for a stop (with realtime)
/// GET /operator/health                  → transit provider health for selected country instance
/// GET /operator/transport-types         → transport types with available icons
/// </summary>
[ApiController]
[Route("[controller]")]
public class OperatorController : ControllerBase
{
    private readonly OperatorManager _manager;
    private readonly TicketableCatalogueService _ticketable;
    private readonly TransitDataService _transitData;

    public OperatorController(
        OperatorManager manager,
        TicketableCatalogueService ticketable,
        TransitDataService transitData)
    {
        _manager = manager;
        _ticketable = ticketable;
        _transitData = transitData;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<OperatorDto>>>> GetAll(
        [FromQuery] int? countryId = null)
    {
        var operators = await _manager.GetAllOperatorsAsync(countryId);
        return Ok(OperationResult<List<OperatorDto>>.Ok(operators));
    }

    [HttpGet("ticketable")]
    public async Task<ActionResult<OperationResult<List<TicketableOperatorDto>>>> GetTicketable(
        [FromQuery] int? countryId = null)
    {
        var operators = await _ticketable.GetTicketableOperatorsAsync(countryId);
        return Ok(OperationResult<List<TicketableOperatorDto>>.Ok(operators));
    }

    [HttpGet("stops")]
    public async Task<ActionResult<OperationResult<List<StopDto>>>> GetStops(
        [FromQuery] int? countryId = null)
    {
        var stops = await _transitData.GetAllStopsAsync(countryId);
        return Ok(OperationResult<List<StopDto>>.Ok(stops));
    }

    [HttpGet("routes")]
    public async Task<ActionResult<OperationResult<List<RouteDto>>>> GetRoutes(
        [FromQuery] int? countryId = null)
    {
        var routes = await _transitData.GetAllRoutesAsync(countryId);
        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    [HttpGet("stops/{stopId}/schedule")]
    public async Task<ActionResult<OperationResult<StopScheduleDto>>> GetStopSchedule(
        string stopId,
        [FromQuery] int? countryId = null)
    {
        var schedule = await _transitData.GetStopScheduleAsync(stopId, countryId);
        if (schedule is null)
            return NotFound(OperationResult<StopScheduleDto>.Fail(
                $"Stop '{stopId}' not found"));

        return Ok(OperationResult<StopScheduleDto>.Ok(schedule));
    }

    [HttpGet("health")]
    public async Task<ActionResult<OperationResult<object>>> GetTransitHealth(
        [FromQuery] int? countryId = null)
    {
        var ok = await _transitData.IsTransitHealthyAsync(countryId);
        if (!ok)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                OperationResult<object>.Fail("Transit provider is unavailable"));

        return Ok(OperationResult<object>.Ok(new { healthy = true }));
    }

    [HttpGet("transport-types")]
    public async Task<ActionResult<OperationResult<List<TransportTypeDto>>>> GetTransportTypes()
    {
        var types = await _manager.GetTransportTypesAsync();
        return Ok(OperationResult<List<TransportTypeDto>>.Ok(types));
    }
}
