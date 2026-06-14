using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class OperatorsController : ControllerBase
{
    private readonly OperatorService _operatorService;

    public OperatorsController(OperatorService operatorService) { _operatorService = operatorService; }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<OperatorDto>>>> GetAll(
        [FromQuery] int? countryId = null,
        [FromQuery] OperatorType? type = null,
        CancellationToken ct = default)
    {
        var result = await _operatorService.GetAllAsync(countryId, type, ct);
        return Ok(OperationResult<List<OperatorDto>>.Ok(result));
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<OperationResult<OperatorDto>>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var op = await _operatorService.GetByGlobalIdAsync(globalId, ct);
        if (op is null) return NotFound(OperationResult<OperatorDto>.Fail("Operator not found."));
        return Ok(OperationResult<OperatorDto>.Ok(op));
    }

    [HttpGet("{globalId}/stations")]
    public async Task<ActionResult<OperationResult<List<StationDto>>>> GetStations(string globalId, CancellationToken ct = default)
    {
        var stations = await _operatorService.GetStationsAsync(globalId, ct);
        return Ok(OperationResult<List<StationDto>>.Ok(stations));
    }

    [HttpGet("{globalId}/routes")]
    public async Task<ActionResult<OperationResult<List<RouteDto>>>> GetRoutes(string globalId, CancellationToken ct = default)
    {
        var routes = await _operatorService.GetRoutesAsync(globalId, ct);
        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    [HttpGet("{globalId}/feeds")]
    public async Task<ActionResult<OperationResult<List<FeedDto>>>> GetFeeds(string globalId, CancellationToken ct = default)
    {
        var feeds = await _operatorService.GetFeedsAsync(globalId, ct);
        return Ok(OperationResult<List<FeedDto>>.Ok(feeds));
    }
}
