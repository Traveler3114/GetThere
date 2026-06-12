using Microsoft.AspNetCore.Mvc;

using GetThereShared.Contracts;
using TransitInfoAPI.Core;

namespace TransitInfoAPI.Controllers;

[ApiController]
public sealed class BikeController : ControllerBase
{
    private readonly BikeStationCache _cache;

    public BikeController(BikeStationCache cache)
    {
        _cache = cache;
    }

    [HttpGet("/bike-stations")]
    public ActionResult<List<BikeStationResponse>> GetAllStations(
        [FromQuery] string? countryName = null)
    {
        return Ok(_cache.GetAllStations(countryName));
    }

    [HttpGet("/bike-stations/{providerId}/exists")]
    public ActionResult<bool> HasStationsInCountry(
        int providerId,
        [FromQuery] string countryName)
    {
        return Ok(_cache.HasStationsInCountry(providerId, countryName));
    }
}
