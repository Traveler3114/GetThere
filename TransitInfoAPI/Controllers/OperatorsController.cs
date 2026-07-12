using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class OperatorsController : ControllerBase
{
    private readonly OperatorManager _operatorService;

public OperatorsController(OperatorManager operatorManager) { _operatorService = operatorManager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult> GetAll(
        [FromQuery] string? q = null,
        [FromQuery] string? format = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        if (format == "geojson")
        {
            var fc = await _operatorService.GetAllGeoJsonAsync(page, perPage, ct);
            return Ok(fc);
        }

        var result = await _operatorService.GetAllAsync(q, page, perPage, ct);
        var total = await _operatorService.GetTotalCountAsync(q, ct);
        return Ok(new Paginated<OperatorResponse>(result, total, page, perPage));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult<OperatorResponse>> GetById(int id, CancellationToken ct = default)
    {
        var op = await _operatorService.GetByIdAsync(id, ct);
        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("by-onestop/{onestopId}")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult<OperatorResponse>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var op = await _operatorService.GetByOnestopIdAsync(onestopId, ct);
        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("{globalId}")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult<OperatorResponse>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var op = await _operatorService.GetByGlobalIdAsync(globalId, ct);
        if (op is null) return NotFound();
        return Ok(op);
    }

    [HttpGet("types")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult<List<object>>> GetTypes()
    {
        var types = await _operatorService.GetTypesAsync();
        return Ok(types);
    }

    [HttpGet("{id:int}/service-area")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult> GetServiceArea(int id, CancellationToken ct = default)
    {
        var result = await _operatorService.GetServiceAreaAsync(id, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpGet("{globalId}/stations")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult<List<StationResponse>>> GetStations(string globalId, CancellationToken ct = default)
    {
        var stations = await _operatorService.GetStationsAsync(globalId, ct);
        return Ok(stations);
    }

    [HttpGet("{globalId}/routes")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult<List<RouteResponse>>> GetRoutes(string globalId, CancellationToken ct = default)
    {
        var routes = await _operatorService.GetRoutesAsync(globalId, ct);
        return Ok(routes);
    }

    [HttpGet("{globalId}/feeds")]
    [Authorize(Policy = PermissionKeys.OperatorsView)]
    public async Task<ActionResult<List<FeedResponse>>> GetFeeds(string globalId, CancellationToken ct = default)
    {
        var feeds = await _operatorService.GetFeedsAsync(globalId, ct);
        return Ok(feeds);
    }

    [Authorize(Policy = PermissionKeys.OperatorsManage)]
    [HttpPost]
    public async Task<ActionResult<OperatorResponse>> Create([FromBody] CreateOperatorRequest request, CancellationToken ct = default)
    {
        var dto = await _operatorService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetByGlobalId), new { globalId = dto.GlobalId }, dto);
    }

    [Authorize(Policy = PermissionKeys.OperatorsManage)]
    [HttpPut("{globalId}")]
    public async Task<ActionResult> Update(string globalId, [FromBody] UpdateOperatorRequest request, CancellationToken ct = default)
    {
        var success = await _operatorService.UpdateAsync(globalId, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [Authorize(Policy = PermissionKeys.OperatorsManage)]
    [HttpDelete("{globalId}")]
    public async Task<ActionResult> Delete(string globalId, CancellationToken ct = default)
    {
        var success = await _operatorService.DeleteAsync(globalId, ct);
        if (!success) return NotFound();
        return NoContent();
    }
}