using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

/// <summary>
/// All transit-related endpoints.
///
/// GET /operator                    → list of all operators
/// GET /operator/stops              → all stops across all operators
/// GET /operator/routes             → all routes across all operators
/// GET /operator/vehicles           → all live vehicles across all operators
/// GET /operator/stops/{id}/schedule → departures for a stop (with realtime)
/// GET /operator/trips/{id}         → full stop sequence for a trip (with realtime)
/// </summary>
[ApiController]
[Route("[controller]")]
public class OperatorController : ControllerBase
{
    private readonly OperatorManager _manager;

    public OperatorController(OperatorManager manager)
    {
        _manager = manager;
    }

    // GET /operator
    [HttpGet]
    public async Task<ActionResult<OperationResult<List<OperatorDto>>>> GetAll()
    {
        var operators = await _manager.GetAllOperatorsAsync();
        return Ok(OperationResult<List<OperatorDto>>.Ok(operators));
    }

    // GET /operator/stops
    [HttpGet("stops")]
    public ActionResult<OperationResult<List<StopDto>>> GetStops()
    {
        var stops = _manager.GetAllStops();
        return Ok(OperationResult<List<StopDto>>.Ok(stops));
    }

    // GET /operator/routes
    [HttpGet("routes")]
    public ActionResult<OperationResult<List<RouteDto>>> GetRoutes()
    {
        var routes = _manager.GetAllRoutes();
        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    // GET /operator/vehicles
    [HttpGet("vehicles")]
    public ActionResult<OperationResult<List<VehicleDto>>> GetVehicles()
    {
        var vehicles = _manager.GetAllVehicles();
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
}