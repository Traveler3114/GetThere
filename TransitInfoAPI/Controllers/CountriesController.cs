using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Common;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class CountriesController : ControllerBase
{
    private readonly CountryManager _countryService;

public CountriesController(CountryManager countryService) { _countryService = countryService; }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var countries = await _countryService.GetAllAsync(page, perPage, ct);
        var total = await _countryService.GetTotalCountAsync(ct);
        return Ok(new Paginated<CountryResponse>(countries, total, page, perPage));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<CountryResponse>> Create([FromBody] CreateCountryRequest request, CancellationToken ct = default)
    {
        var dto = await _countryService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), null, dto);
    }
}
