using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

/// <summary>
/// Address search for the trip planner's start/end fields. Reached same-origin by the public map
/// page, consistent with the map's other <c>[AllowAnonymous]</c> surfaces — but separately
/// rate-limited because each call costs money at Azure Maps. The subscription key stays server-side.
/// </summary>
[ApiController]
[Route("geocode")]
[AllowAnonymous]
[EnableRateLimiting("Geocode")]
public class GeocodingController(GeocodingManager geocoding, ILogger<GeocodingController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query 'q' is required.");

        try
        {
            var results = await geocoding.SearchAsync(q, ct);
            return Ok(results);
        }
        catch (GeocodingException ex)
        {
            logger.LogWarning(ex, "Geocoding failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }
}
