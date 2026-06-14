using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class RoutesController : ControllerBase
{
    private readonly RouteService _routeService;

    public RoutesController(RouteService routeService) { _routeService = routeService; }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<RouteDto>>>> GetAll(
        [FromQuery] int? operatorId,
        [FromQuery] RouteType? routeType,
        CancellationToken ct = default)
    {
        var result = await _routeService.GetAllAsync(operatorId, routeType, ct);
        return Ok(OperationResult<List<RouteDto>>.Ok(result));
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<OperationResult<RouteDto>>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var route = await _routeService.GetByGlobalIdAsync(globalId, ct);
        if (route is null) return NotFound(OperationResult<RouteDto>.Fail("Route not found."));
        return Ok(OperationResult<RouteDto>.Ok(route));
    }
}
