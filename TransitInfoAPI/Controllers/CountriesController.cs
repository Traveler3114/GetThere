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
    public async Task<ActionResult<Paginated<CountryDto>>> GetAll(
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
            .Select(c => new CountryDto
            {
                Id = c.Id,
                Name = c.Name,
                IsoCode = c.IsoCode,
                Continent = c.Continent
            })
            .ToListAsync(ct);

        return Ok(new Paginated<CountryDto>(countries, total));
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateCountryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(statusCode: 400, title: "Country name is required.");

        if (string.IsNullOrWhiteSpace(request.IsoCode))
            return Problem(statusCode: 400, title: "ISO code is required.");

        var exists = await _db.Countries.AnyAsync(c => c.IsoCode == request.IsoCode, ct);
        if (exists)
            return Problem(statusCode: 409, title: $"Country with ISO code '{request.IsoCode}' already exists.");

        var country = new Country
        {
            Name = request.Name,
            IsoCode = request.IsoCode,
            Continent = request.Continent
        };
        _db.Countries.Add(country);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetAll), null);
    }
}
