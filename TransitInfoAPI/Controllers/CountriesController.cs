using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Models;

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
    public async Task<ActionResult<Paginated<Country>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.Countries
            .OrderBy(c => c.Id)
            .AsQueryable();

        var total = await query.CountAsync(ct);
        var countries = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(ct);

        return Ok(new Paginated<Country>(countries, total));
    }

    [HttpPost]
    public async Task<ActionResult<Country>> Create([FromBody] Country country, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(country.Name))
            return Problem(statusCode: 400, title: "Country name is required.");

        if (string.IsNullOrWhiteSpace(country.IsoCode))
            return Problem(statusCode: 400, title: "ISO code is required.");

        var exists = await _db.Countries.AnyAsync(c => c.IsoCode == country.IsoCode, ct);
        if (exists)
            return Problem(statusCode: 409, title: $"Country with ISO code '{country.IsoCode}' already exists.");

        _db.Countries.Add(country);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetAll), null, country);
    }
}
