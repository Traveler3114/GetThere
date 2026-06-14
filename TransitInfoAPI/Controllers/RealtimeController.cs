using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("realtime")]
public class RealtimeController : ControllerBase
{
    private readonly RealtimeService _realtime;

    public RealtimeController(RealtimeService realtime) { _realtime = realtime; }

    [HttpGet("vehicles")]
    public async Task<ActionResult<OperationResult<List<VehicleDto>>>> GetVehicles(
        [FromQuery] string? operatorGlobalId = null,
        [FromQuery] double? lat = null,
        [FromQuery] double? lon = null,
        [FromQuery] double? radiusKm = null,
        CancellationToken ct = default)
    {
        var vehicles = await _realtime.GetVehiclesAsync(operatorGlobalId, lat, lon, radiusKm, ct);
        return Ok(OperationResult<List<VehicleDto>>.Ok(vehicles));
    }
}
