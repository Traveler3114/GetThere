using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
[Authorize(Policy = PermissionKeys.PlacesView)]
public class PlacesController : ControllerBase
{
    private readonly PlaceManager _placeManager;

public PlacesController(PlaceManager placeManager) { _placeManager = placeManager; }

    [HttpGet]
    public async Task<ActionResult<Paginated<PlaceResponse>>> GetAll(
        [FromQuery] string? countryCode = null,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var places = await _placeManager.GetAllAsync(countryCode, page, perPage, ct);
        var total = await _placeManager.GetTotalCountAsync(countryCode, ct);
        return Ok(new Paginated<PlaceResponse>(places, total, page, perPage));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PlaceResponse>> GetById(int id, CancellationToken ct = default)
    {
        var place = await _placeManager.GetByIdAsync(id, ct);
        if (place is null) return NotFound();
        return Ok(place);
    }

    [HttpGet("{id}/operators")]
    public async Task<ActionResult<List<OperatorResponse>>> GetOperators(int id, CancellationToken ct = default)
    {
        var operators = await _placeManager.GetOperatorsAsync(id, ct);
        return Ok(operators);
    }

    [HttpGet("{id}/stations")]
    public async Task<ActionResult<List<StationResponse>>> GetStations(int id, CancellationToken ct = default)
    {
        var stations = await _placeManager.GetStationsAsync(id, ct);
        return Ok(stations);
    }
}