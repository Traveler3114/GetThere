using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("mobility")]
public class MobilityController : ControllerBase
{
    private readonly TransitDbContext _db;
    private readonly MobilityManager _mobility;

    public MobilityController(TransitDbContext db, MobilityManager mobility) { _db = db; _mobility = mobility; }

    [HttpGet("stations")]
    public async Task<ActionResult<Paginated<MobilityStationDto>>> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.MobilityStations
            .Include(ms => ms.MobilityProvider)
            .ThenInclude(mp => mp.Operator)
            .OrderBy(ms => ms.Id)
            .AsQueryable();

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / 111.0;
            var lonRange = radiusKm.Value / (111.0 * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(ms =>
                ms.Latitude >= lat.Value - latRange &&
                ms.Latitude <= lat.Value + latRange &&
                ms.Longitude >= lon.Value - lonRange &&
                ms.Longitude <= lon.Value + lonRange);
        }

        var total = await query.CountAsync(ct);
        var stations = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(ms => new MobilityStationDto
            {
                Id = ms.Id,
                StationId = ms.StationId,
                Name = ms.Name,
                Latitude = ms.Latitude,
                Longitude = ms.Longitude,
                AvailableVehicles = ms.AvailableVehicles,
                Capacity = ms.Capacity,
                ProviderName = ms.MobilityProvider.Operator.Name,
                LastUpdated = ms.LastUpdated
            })
            .ToListAsync(ct);

        return Ok(new Paginated<MobilityStationDto>(stations, total, page, perPage));
    }

    [HttpPost("providers/{id}/poll")]
    public async Task<IActionResult> PollProvider(int id, CancellationToken ct = default)
    {
        await _mobility.PollMobilityProviderAsync(id, ct);
        return NoContent();
    }
}
