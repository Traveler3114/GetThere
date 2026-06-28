using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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
    public async Task<ActionResult> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] string? format = null,
        [FromQuery] string? countryName = null,
        CancellationToken ct = default)
    {
        var query = _db.MobilityStations
            .Include(ms => ms.Operator)
            .Include(ms => ms.Country)
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

        if (!string.IsNullOrWhiteSpace(countryName))
            query = query.Where(ms => ms.Country.Name == countryName);

        if (string.Equals(format, "geojson", StringComparison.OrdinalIgnoreCase))
        {
            var stations = await query.ToListAsync(ct);
            var features = stations.Select(s => new
            {
                type = "Feature",
                geometry = new { type = "Point", coordinates = new[] { s.Longitude, s.Latitude } },
                properties = new
                {
                    id = "mob_" + s.Id,
                    name = s.Name,
                    providerName = s.Operator?.Name ?? "",
                    stationId = s.StationId,
                    capacity = s.Capacity,
                    availableVehicles = s.AvailableVehicles,
                    lastUpdated = s.LastUpdated,
                    countryName = s.Country?.Name,
                    countryCode = s.Country?.IsoCode
                }
            }).ToList();
            return Ok(new { type = "FeatureCollection", features });
        }

        var total = await query.CountAsync(ct);
        query = query.OrderBy(ms => ms.Id);
        var paged = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);

        var dtos = paged.Select(MobilityStationMapper.ToResponse).ToList();
        return Ok(new Paginated<MobilityStationResponse>(dtos, total, page, perPage));
    }

    [HttpGet("countries")]
    public async Task<ActionResult<List<string>>> GetCountries(CancellationToken ct)
    {
        var names = await _db.MobilityStations
            .Where(ms => ms.Country != null)
            .Select(ms => ms.Country.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
        return Ok(names);
    }
}
