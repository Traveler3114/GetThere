using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("realtime")]
[Authorize]
public class RealtimeController : ControllerBase
{
    private readonly RealtimeManager _realtime;

    public RealtimeController(RealtimeManager realtime) { _realtime = realtime; }

    [HttpGet("vehicles")]
    [Authorize(Policy = PermissionKeys.RealtimeView)]
    public async Task<ActionResult<List<VehicleResponse>>> GetVehicles(
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

    [HttpGet("tripupdates")]
    [Authorize(Policy = PermissionKeys.RealtimeView)]
    public ActionResult<List<TripUpdateResponse>> GetTripUpdates(
        [FromQuery] string? routeId = null,
        CancellationToken ct = default)
    {
        var updates = _realtime.GetTripUpdates(routeId);
        return Ok(updates);
    }

    [HttpGet("alerts")]
    [Authorize(Policy = PermissionKeys.RealtimeView)]
    public async Task<ActionResult<List<AlertResponse>>> GetAlerts(
        [FromQuery] string? stopOnestopId = null,
        [FromQuery] string? routeOnestopId = null,
        CancellationToken ct = default)
    {
        var alerts = await _realtime.GetAlertsAsync(stopOnestopId, routeOnestopId, ct);
        return Ok(alerts);
    }
}