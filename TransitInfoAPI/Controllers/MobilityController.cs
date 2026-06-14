using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("mobility")]
public class MobilityController : ControllerBase
{
    private readonly MobilityService _mobility;

    public MobilityController(MobilityService mobility) { _mobility = mobility; }

    [HttpGet("stations")]
    public async Task<ActionResult<OperationResult<List<MobilityStationDto>>>> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        var stations = await _mobility.GetStationsAsync(lat, lon, radiusKm, ct);
        var result = stations.Select(s => new MobilityStationDto
        {
            StationId = s.StationId,
            Name = s.Name,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            AvailableVehicles = s.AvailableVehicles,
            Capacity = s.Capacity,
            ProviderName = s.MobilityProvider.Operator.Name
        }).ToList();

        return Ok(OperationResult<List<MobilityStationDto>>.Ok(result));
    }
}
