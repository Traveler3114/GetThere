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

        // Resolve the filter: an explicit structured preference wins, else the named preset, else the
        // default (fastest). Apply its request-shaping to OTP, then rank the returned set by it.
        var preference = request.Preference ?? RoutingPresets.Resolve(request.Preset) ?? new RoutingPreference();
        var shaped = request with
        {
            TransitModes = request.TransitModes ?? RoutingPresets.AllowedTransitModes(preference),
            TransferPenalty = request.TransferPenalty ?? preference.TransferPenalty,
            WalkReluctance = request.WalkReluctance ?? preference.WalkReluctance,
        };

        try
        {
            var itineraries = await otp.PlanAsync(shaped, ct);
            return Ok(ItineraryRanker.Rank(itineraries, preference.RankBy));
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
