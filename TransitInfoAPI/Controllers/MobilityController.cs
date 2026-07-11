using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("mobility")]
public class MobilityController : ControllerBase
{
    private readonly MobilityManager _mobility;

public MobilityController(MobilityManager mobility) { _mobility = mobility; }

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
        if (string.Equals(format, "geojson", StringComparison.OrdinalIgnoreCase))
        {
            var fc = await _mobility.GetAllGeoJsonAsync(lat, lon, radiusKm, null, 5000, ct);
            return Ok(fc);
        }

        var result = await _mobility.GetAllAsync(lat, lon, radiusKm, null, page, perPage, ct);
        var total = await _mobility.GetTotalCountAsync(lat, lon, radiusKm, null, ct);
        return Ok(new Paginated<MobilityStationResponse>(result, total, page, perPage));
    }

    [HttpGet("countries")]
    public async Task<ActionResult<List<string>>> GetCountries(CancellationToken ct = default)
    {
        var countries = await _mobility.GetCountriesAsync(ct);
        return Ok(countries);
    }
}