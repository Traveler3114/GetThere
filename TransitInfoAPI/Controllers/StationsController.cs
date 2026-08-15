using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

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
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
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

    /// <summary>
    /// Station name search, read anonymously by the public map page's search box.
    /// <para>
    /// This required <c>stations.view</c> while the map was served by GetThereAPI and proxied here
    /// under the service account. The map now loads from this service and calls it same-origin as an
    /// ordinary browser, so it holds no credential. It returns nothing <see cref="GetAll"/> does not
    /// already serve anonymously — the same station rows, selected by name rather than by radius.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    [HttpGet("search")]
    public async Task<ActionResult<Paginated<StationResponse>>> Search(
        [FromQuery] string? q,
        [FromQuery] RouteType? routeType,
        [FromQuery] int? countryId,
        [FromQuery] string? countryName = null,
        [FromQuery] string? stationType = null,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var result = await _stationService.SearchAsync(q, routeType, countryId, countryName, stationType, page, perPage, ct);
        // q and routeType are passed through: without them the total counted every station in the
        // country instead of the ones the search matched.
        var total = await _stationService.GetTotalCountAsync(null, null, null, countryId, countryName, stationType, q, routeType, ct);
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
        // Bounded because ScheduleManager multiplies this by its over-fetch factor: an unbounded
        // count overflowed the multiply to a negative scan limit and threw, so ?count=2147483647
        // was a 500 on an anonymous endpoint. 100 departures is far past what any caller renders.
        [FromQuery, Range(1, 100)] int count = 10,
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
