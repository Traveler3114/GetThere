using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Routing.Otp;

namespace TransitInfoAPI.Controllers;

/// <summary>
/// Door-to-door journey planning. Delegates to OTP (fed by the export bundle) and returns itineraries
/// whose transit legs carry the operator's TransitInfo GlobalId, so a client can later join to
/// GetThereAPI ticketing without either server calling the other.
/// <para>
/// This is the planning half of the map-only client exception in AGENTS.md: the client reads it
/// directly from TransitInfoAPI, same-origin, like the map endpoints.
/// </para>
/// </summary>
[ApiController]
[Route("plan")]
[AllowAnonymous]
public class PlanController(OtpGraphQlClient otp, ILogger<PlanController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Plan([FromBody] PlanRequest request, CancellationToken ct = default)
    {
        if (!GeoBounds.IsUsable(request.FromLat, request.FromLon) || !GeoBounds.IsUsable(request.ToLat, request.ToLon))
            return BadRequest("Origin and destination must be valid coordinates.");

        try
        {
            var itineraries = await otp.PlanAsync(request, ct);
            return Ok(itineraries);
        }
        catch (OtpPlanException ex)
        {
            // OTP is down/misconfigured or rejected the query — a 502 tells the client the upstream
            // failed, distinct from "planned fine, no route".
            logger.LogWarning(ex, "Journey plan failed");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}
