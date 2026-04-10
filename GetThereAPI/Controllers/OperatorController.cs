using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;

namespace GetThereAPI.Controllers;

/// <summary>
/// All transit-related endpoints.
///
/// GET /operator                         → list of all operators (optional ?countryId=)
/// GET /operator/ticketable              → operators available for ticket purchase (optional ?countryId=)
/// GET /operator/stops                   → all stops across all operators (optional ?countryId=)
/// GET /operator/routes                  → all routes across all operators (optional ?countryId=)
/// GET /operator/vehicles                → all live vehicles across all operators (optional ?countryId=)
/// GET /operator/stops/{id}/schedule     → departures for a stop (with realtime)
/// GET /operator/trips/{id}              → full stop sequence for a trip (with realtime)
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
        [FromQuery] int? countryId = null,
        CancellationToken cancellationToken = default)
    {
        var stops = await _manager.GetAllStopsAsync(countryId, cancellationToken);
        return Ok(OperationResult<List<StopDto>>.Ok(stops));
    }

    /// <summary>Returns all routes, optionally filtered by country.</summary>
    // GET /operator/routes
    // GET /operator/routes?countryId=1
    [HttpGet("routes")]
    public ActionResult<OperationResult<List<RouteDto>>> GetRoutes(
        [FromQuery] int? countryId = null)
    {
        var routes = _manager.GetAllRoutes(countryId);
        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    /// <summary>Returns all live vehicles, optionally filtered by country.</summary>
    // GET /operator/vehicles
    // GET /operator/vehicles?countryId=1
    [HttpGet("vehicles")]
    public ActionResult<OperationResult<List<VehicleDto>>> GetVehicles(
        [FromQuery] int? countryId = null)
    {
        var vehicles = _manager.GetAllVehicles(countryId);
        return Ok(OperationResult<List<VehicleDto>>.Ok(vehicles));
    }

    // GET /operator/stops/{stopId}/schedule
    [HttpGet("stops/{stopId}/schedule")]
    public async Task<ActionResult<OperationResult<StopScheduleDto>>> GetStopSchedule(
        string stopId)
    {
        var schedule = await _manager.GetStopScheduleAsync(stopId);
        if (schedule is null)
            return NotFound(OperationResult<StopScheduleDto>.Fail(
                $"Stop '{stopId}' not found"));

        return Ok(OperationResult<StopScheduleDto>.Ok(schedule));
    }

    // GET /operator/trips/{tripId}
    [HttpGet("trips/{tripId}")]
    public async Task<ActionResult<OperationResult<TripDetailDto>>> GetTripDetail(
        string tripId)
    {
        var detail = await _manager.GetTripDetailAsync(tripId);
        if (detail is null)
            return NotFound(OperationResult<TripDetailDto>.Fail(
                $"Trip '{tripId}' not found"));

        return Ok(OperationResult<TripDetailDto>.Ok(detail));
    }

    // GET /operator/transport-types
    [HttpGet("transport-types")]
    public async Task<ActionResult<OperationResult<List<TransportTypeDto>>>> GetTransportTypes()
    {
        var types = await _manager.GetTransportTypesAsync(_env);
        return Ok(OperationResult<List<TransportTypeDto>>.Ok(types));
    }
}
