using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

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
    public async Task<ActionResult<OperationResult<List<OperatorResponse>>>> GetAll(
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        var operators = await _manager.GetAllOperatorsAsync(countryId, ct);
        return Ok(OperationResult<List<OperatorResponse>>.Ok(operators));
    }

    [HttpGet("ticketable")]
    public async Task<ActionResult<OperationResult<List<TicketableOperatorResponse>>>> GetTicketable(
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        var operators = await _ticketable.GetTicketableOperatorsAsync(countryId, ct);
        return Ok(OperationResult<List<TicketableOperatorResponse>>.Ok(operators));
    }

    [HttpGet("stops")]
    public async Task<ActionResult<OperationResult<List<StopResponse>>>> GetStops(
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        var stops = await _transitData.GetAllStopsAsync(countryId, ct);
        return Ok(OperationResult<List<StopResponse>>.Ok(stops));
    }

    [HttpGet("routes")]
    public async Task<ActionResult<OperationResult<List<RouteResponse>>>> GetRoutes(
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        var routes = await _transitData.GetAllRoutesAsync(countryId, ct);
        return Ok(OperationResult<List<RouteResponse>>.Ok(routes));
    }

    [HttpGet("stops/{stopId}/schedule")]
    public async Task<ActionResult<OperationResult<StopScheduleResponse>>> GetStopSchedule(
        string stopId,
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        var schedule = await _transitData.GetStopScheduleAsync(stopId, countryId, ct);
        if (schedule is null)
            return NotFound(OperationResult<StopScheduleResponse>.Fail("STOP_NOT_FOUND",
                $"Stop '{stopId}' not found"));

        return Ok(OperationResult<StopScheduleResponse>.Ok(schedule));
    }

    [HttpGet("health")]
    public async Task<ActionResult<OperationResult<object>>> GetTransitHealth(
        [FromQuery] int? countryId = null,
        CancellationToken ct = default)
    {
        var ok = await _transitData.IsTransitHealthyAsync(countryId, ct);
        if (!ok)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                OperationResult<object>.Fail("Transit provider is unavailable"));

        return Ok(OperationResult<object>.Ok(new { healthy = true }));
    }

    [HttpGet("transport-types")]
    public async Task<ActionResult<OperationResult<List<TransportTypeResponse>>>> GetTransportTypes(CancellationToken ct = default)
    {
        var types = await _manager.GetTransportTypesAsync(ct);
        return Ok(OperationResult<List<TransportTypeResponse>>.Ok(types));
    }
}
