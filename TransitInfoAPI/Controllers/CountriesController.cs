using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Data;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Mapping;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class CountriesController : ControllerBase
{
    private readonly CountryManager _countryService;

    public CountriesController(CountryManager countryService) { _countryService = countryService; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.CountriesView)]
    public async Task<ActionResult> GetAll(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var countries = await _countryService.GetAllAsync(page, perPage, ct);
        var total = await _countryService.GetTotalCountAsync(ct);
        return Ok(new Paginated<CountryResponse>(countries, total, page, perPage));
    }

    [Authorize(Policy = PermissionKeys.CountriesManage)]
    [HttpPost]
    public async Task<ActionResult<CountryResponse>> Create([FromBody] CreateCountryRequest request, CancellationToken ct = default)
    {
        var dto = await _countryService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), null, dto);
    }
}
