using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

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
    private readonly IWebHostEnvironment _env;

    public OperatorController(OperatorManager manager, IWebHostEnvironment env)
    {
        _manager = manager;
        _env = env;
    }

    /// <summary>Returns all operators, optionally filtered by country.</summary>
    // GET /operator
    // GET /operator?countryId=1
    [HttpGet]
    public async Task<ActionResult<OperationResult<List<OperatorDto>>>> GetAll(
        [FromQuery] int? countryId = null)
    {
        var operators = await _manager.GetAllOperatorsAsync(countryId);
        return Ok(OperationResult<List<OperatorDto>>.Ok(operators));
    }

    /// <summary>
    /// Returns operators available for ticket purchase, optionally filtered by country.
    /// </summary>
    // GET /operator/ticketable
    // GET /operator/ticketable?countryId=1
    [HttpGet("ticketable")]
    public async Task<ActionResult<OperationResult<List<TicketableOperatorDto>>>> GetTicketable(
        [FromQuery] int? countryId = null)
    {
        var operators = await _manager.GetTicketableOperatorsAsync(countryId);
        return Ok(OperationResult<List<TicketableOperatorDto>>.Ok(operators));
    }

    /// <summary>Returns all stops, optionally filtered by country.</summary>
    // GET /operator/stops
    // GET /operator/stops?countryId=1
    [HttpGet("stops")]
    public async Task<ActionResult<OperationResult<List<StopDto>>>> GetStops(
        [FromQuery] int? countryId = null)
    {
        var stops = await _manager.GetAllStopsAsync(countryId);
        return Ok(OperationResult<List<StopDto>>.Ok(stops));
    }

    /// <summary>Returns all routes, optionally filtered by country.</summary>
    // GET /operator/routes
    // GET /operator/routes?countryId=1
    [HttpGet("routes")]
    public async Task<ActionResult<OperationResult<List<RouteDto>>>> GetRoutes(
        [FromQuery] int? countryId = null)
    {
        var routes = await _manager.GetAllRoutesAsync(countryId);
        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    // GET /operator/stops/{stopId}/schedule
    [HttpGet("stops/{stopId}/schedule")]
    public async Task<ActionResult<OperationResult<StopScheduleDto>>> GetStopSchedule(
        string stopId,
        [FromQuery] int? countryId = null)
    {
        var schedule = await _manager.GetStopScheduleAsync(stopId, countryId);
        if (schedule is null)
            return NotFound(OperationResult<StopScheduleDto>.Fail(
                $"Stop '{stopId}' not found"));

        return Ok(OperationResult<StopScheduleDto>.Ok(schedule));
    }

    // GET /operator/health
    // GET /operator/health?countryId=1
    [HttpGet("health")]
    public async Task<ActionResult<OperationResult<object>>> GetTransitHealth(
        [FromQuery] int? countryId = null)
    {
        var ok = await _manager.IsTransitHealthyAsync(countryId);
        if (!ok)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                OperationResult<object>.Fail("Transit provider is unavailable"));

        return Ok(OperationResult<object>.Ok(new { healthy = true }));
    }

    // GET /operator/transport-types
    [HttpGet("transport-types")]
    public async Task<ActionResult<OperationResult<List<TransportTypeDto>>>> GetTransportTypes()
    {
        var types = await _manager.GetTransportTypesAsync(_env);
        return Ok(OperationResult<List<TransportTypeDto>>.Ok(types));
    }
}
