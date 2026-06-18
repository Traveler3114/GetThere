using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Models;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("realtime")]
public class RealtimeController : ControllerBase
{
    private readonly RealtimeManager _realtime;

    public RealtimeController(RealtimeManager realtime) { _realtime = realtime; }

    [HttpGet("vehicles")]
    public async Task<ActionResult<List<VehicleDto>>> GetVehicles(
        [FromQuery] string? feedId = null,
        [FromQuery] double? minLat = null,
        [FromQuery] double? minLon = null,
        [FromQuery] double? maxLat = null,
        [FromQuery] double? maxLon = null,
        CancellationToken ct = default)
    {
        var vehicles = await _realtime.GetVehiclesAsync(feedId, minLat, minLon, maxLat, maxLon, ct);
        return Ok(vehicles);
    }

    [HttpGet("alerts")]
    public async Task<List<AlertDto>> GetAlerts(
        [FromQuery] string? stopOnestopId = null,
        [FromQuery] string? routeOnestopId = null,
        CancellationToken ct = default)
    {
        return await _realtime.GetAlertsAsync(stopOnestopId, routeOnestopId, ct);
    }
}
