using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("mobility")]
public class MobilityController : ControllerBase
{
    private readonly MobilityService _mobility;

    public MobilityController(MobilityService mobility) { _mobility = mobility; }

    [HttpGet("stations")]
    public async Task<ActionResult<List<object>>> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var stations = await _mobility.GetStationsAsync(lat, lon, radiusKm, ct);
        var result = stations.Select(s => new
        {
            s.StationId,
            s.Name,
            s.Latitude,
            s.Longitude,
            s.AvailableVehicles,
            s.Capacity,
            ProviderName = s.MobilityProvider.Operator.Name
        }).ToList();

        return Ok(result);
    }
}
