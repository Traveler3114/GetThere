using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class CountriesController : ControllerBase
{
    private readonly TransitDbContext _db;

    public CountriesController(TransitDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<Country>>>> GetAll(
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.Countries
            .OrderBy(c => c.Id)
            .AsQueryable();

        if (after > 0)
            query = query.Where(c => c.Id > after);

        var countries = await query
            .Take(perPage)
            .ToListAsync(ct);

        var nextAfter = countries.Count > 0 ? countries.Last().Id : after;
        var total = await _db.Countries.CountAsync(ct);
        var nextUrl = countries.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<Country>>.OkPaginated(countries, nextAfter, total, nextUrl));
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<Country>>> Create([FromBody] Country country, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(country.Name))
            return BadRequest(OperationResult<Country>.Fail("Country name is required."));

        if (string.IsNullOrWhiteSpace(country.IsoCode))
            return BadRequest(OperationResult<Country>.Fail("ISO code is required."));

        var exists = await _db.Countries.AnyAsync(c => c.IsoCode == country.IsoCode, ct);
        if (exists)
            return Conflict(OperationResult<Country>.Fail($"Country with ISO code '{country.IsoCode}' already exists."));

        _db.Countries.Add(country);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetAll), null, OperationResult<Country>.Ok(country, "Country created."));
    }
}
