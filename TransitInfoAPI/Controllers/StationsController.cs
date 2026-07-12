using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class StationsController : ControllerBase
{
    private readonly StationManager _stationService;

public StationsController(StationManager stationManager) { _stationService = stationManager; }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> GetAll(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int? countryId,
        [FromQuery] string? format = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        if (format == "geojson")
        {
            var fc = await _stationService.GetAllGeoJsonAsync(lat, lon, radiusKm, countryId, 5000, ct);
            return Ok(fc);
        }

        var result = await _stationService.GetAllAsync(lat, lon, radiusKm, countryId, page, perPage, ct);
        var total = await _stationService.GetTotalCountAsync(lat, lon, radiusKm, countryId, null, ct: ct);
        return Ok(new Paginated<StationResponse>(result, total, page, perPage));
    }

    [Authorize(Policy = PermissionKeys.StationsView)]
    [HttpGet("search")]
    public async Task<ActionResult<Paginated<StationResponse>>> Search(
        [FromQuery] string? q,
        [FromQuery] RouteType? routeType,
        [FromQuery] int? countryId,
        [FromQuery] string? countryName = null,
        [FromQuery] string? stationType = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var result = await _stationService.SearchAsync(q, routeType, countryId, countryName, stationType, page, perPage, ct);
        var total = await _stationService.GetTotalCountAsync(null, null, null, countryId, countryName, stationType, ct);
        return Ok(new Paginated<StationResponse>(result, total, page, perPage));
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<StationResponse>> GetById(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpGet("by-onestop/{onestopId}")]
    [Authorize(Policy = PermissionKeys.StationsView)]
    public async Task<ActionResult<StationResponse>> GetByOnestopId(string onestopId, CancellationToken ct = default)
    {
        var station = await _stationService.GetByOnestopIdAsync(onestopId, ct);
        if (station is null) return NotFound();
        return Ok(station);
    }

    [HttpGet("{id}/operators")]
    [Authorize(Policy = PermissionKeys.StationsView)]
    public async Task<ActionResult<List<StationOperatorResponse>>> GetOperators(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        var operators = await _stationService.GetOperatorsAsync(station.OnestopId, ct);
        return Ok(operators);
    }

    [HttpGet("{id}/routes")]
    [Authorize(Policy = PermissionKeys.StationsView)]
    public async Task<ActionResult<List<RouteResponse>>> GetRoutes(int id, CancellationToken ct = default)
    {
        var routes = await _stationService.GetRoutesAsync(id, ct);
        return Ok(routes);
    }

    [Authorize(Policy = PermissionKeys.StationsManage)]
    [HttpPost("{id}/rematch-place")]
    public async Task<IActionResult> RematchPlace(int id, CancellationToken ct = default)
    {
        var station = await _stationService.GetByIdAsync(id, ct);
        if (station is null) return NotFound();
        var placeMatching = HttpContext.RequestServices.GetRequiredService<PlaceMatchingManager>();
        await placeMatching.RematchStationAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id}/departures")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DepartureResponse>>> GetDepartures(
        int id,
        [FromQuery] DateTime? from = null,
        [FromQuery] int count = 10,
        CancellationToken ct = default)
    {
        var departures = await _stationService.GetDeparturesAsync(id, from, count, ct);
        return Ok(departures);
    }

    [Authorize(Policy = PermissionKeys.StationsManage)]
    [HttpGet("{id}/reconciliation-detail")]
    public async Task<ActionResult<StationReconciliationDetailResponse>> GetReconciliationDetail(int id, CancellationToken ct = default)
    {
        var detail = await _stationService.GetReconciliationDetailAsync(id, ct);
        if (detail is null) return NotFound();
        return Ok(detail);
    }
}