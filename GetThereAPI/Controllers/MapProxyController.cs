using System.Text.Json;

using GetThereAPI.Common;
using GetThereAPI.Managers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

/// <summary>
/// What remains of the map proxy.
/// <para>
/// This used to front TransitInfoAPI for the map page, which GetThereAPI served: a verbatim
/// passthrough behind an allow-list, plus typed reads for stations, routes, vehicles and departures.
/// None of it has a caller any more. The map page moved to TransitInfoAPI and the client loads it
/// from there, so the page is same-origin with its data and needs no proxy at all — see
/// <c>docs/map-proxy-migration.md</c> for the arrangement this replaced.
/// </para>
/// <para>
/// The one endpoint left is the admin console's reachability probe.
/// </para>
/// </summary>
[ApiController]
[Route("api/map")]
[Authorize]
public class MapProxyController : ControllerBase
{
    private readonly MapManager _mapManager;

    public MapProxyController(MapManager mapManager) { _mapManager = mapManager; }

    /// <summary>
    /// Transport types, read from TransitInfoAPI.
    /// <para>
    /// The admin console calls this to light its "TransitInfoAPI reachable" indicator — any success
    /// means the service answered and the service-account credentials line up. It is the only
    /// remaining consumer, so treat a change here as a change to that probe.
    /// </para>
    /// </summary>
    [HttpGet("transport-types")]
    [Authorize(Policy = PermissionKeys.MapView)]
    public async Task<ActionResult<JsonElement>> GetTransportTypes(CancellationToken ct = default)
    {
        var types = await _mapManager.GetTransportTypesAsync(ct);
        return Ok(types);
    }
}
