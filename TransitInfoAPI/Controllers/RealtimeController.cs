using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("realtime")]
[Authorize]
public class RealtimeController : ControllerBase
{
    private readonly RealtimeManager _realtime;

    public RealtimeController(RealtimeManager realtime) { _realtime = realtime; }

    /// <summary>
    /// Live vehicle positions, read anonymously by the public map page.
    /// <para>
    /// This required <c>realtime.view</c> while the map was served by GetThereAPI and proxied here
    /// under the service account. The map now loads from this service and calls it same-origin as an
    /// ordinary browser, so it holds no credential. Vehicle positions are a public transit fact —
    /// the same data every agency publishes on its own arrivals board — and the global rate limiter
    /// is what bounds scraping. <c>tripupdates</c> and <c>alerts</c> stay gated: nothing public reads
    /// them yet, and an endpoint is far easier to open later than to close.
    /// </para>
    /// </summary>
    [HttpGet("vehicles")]
    [AllowAnonymous]
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
