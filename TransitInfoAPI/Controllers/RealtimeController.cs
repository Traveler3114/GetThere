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
        [FromQuery] string? feedId = null,
        [FromQuery] double? minLat = null,
        [FromQuery] double? minLon = null,
        [FromQuery] double? maxLat = null,
        [FromQuery] double? maxLon = null,
        CancellationToken ct = default)
    {
        var vehicles = await _realtime.GetVehiclesAsync(feedId, minLat, minLon, maxLat, maxLon, ct);
        return Ok(OperationResult<List<VehicleDto>>.Ok(vehicles));
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<OperationResult<List<AlertDto>>>> GetAlerts(
        [FromQuery] string? stopOnestopId = null,
        [FromQuery] string? routeOnestopId = null,
        CancellationToken ct = default)
    {
        var alerts = await _realtime.GetAlertsAsync(stopOnestopId, routeOnestopId, ct);
        return Ok(OperationResult<List<AlertDto>>.Ok(alerts));
    }
}
