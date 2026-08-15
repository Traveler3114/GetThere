using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("mobility")]
[Authorize]
public class MobilityController : ControllerBase
{
    private readonly MobilityManager _mobility;

    public MobilityController(MobilityManager mobility) { _mobility = mobility; }

    [HttpGet("stations")]
    [AllowAnonymous]
    public async Task<ActionResult> GetStations(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] string? format = null,
        [FromQuery] string? countryName = null,
        CancellationToken ct = default)
    {
        if (string.Equals(format, "geojson", StringComparison.OrdinalIgnoreCase))
        {
            var fc = await _mobility.GetAllGeoJsonAsync(lat, lon, radiusKm, null, countryName, 5000, ct);
            return Ok(fc);
        }

        // countryName is now passed through. It was accepted here and handed to nothing, so the
        // admin console's country filter built the query string and the server silently ignored it.
        var result = await _mobility.GetAllAsync(lat, lon, radiusKm, null, countryName, page, perPage, ct);
        var total = await _mobility.GetTotalCountAsync(lat, lon, radiusKm, null, countryName, ct);
        return Ok(new Paginated<MobilityStationResponse>(result, total, page, perPage));
    }

    [HttpGet("countries")]
    [Authorize(Policy = PermissionKeys.MobilityView)]
    public async Task<ActionResult<List<string>>> GetCountries(CancellationToken ct = default)
    {
        var countries = await _mobility.GetCountriesAsync(ct);
        return Ok(countries);
    }
}
