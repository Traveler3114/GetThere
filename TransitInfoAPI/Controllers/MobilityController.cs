using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Common;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("mobility")]
public class MobilityController : ControllerBase
{
    private readonly TransitDbContext _db;
    private readonly MobilityManager _mobility;

    public MobilityController(TransitDbContext db, MobilityManager mobility) { _db = db; _mobility = mobility; }

    [HttpGet("stations")]
    public async Task<ActionResult<Paginated<MobilityStationResponse>>> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
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
            .ToListAsync(ct);

        var dtos = stations.Select(MobilityStationMapper.ToResponse).ToList();

        return Ok(new Paginated<MobilityStationResponse>(dtos, total, page, perPage));
    }

    [HttpPost("providers/{id}/poll")]
    public async Task<IActionResult> PollProvider(int id, CancellationToken ct = default)
    {
        await _mobility.PollMobilityProviderAsync(id, ct);
        return NoContent();
    }
}
