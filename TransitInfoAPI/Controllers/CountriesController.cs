using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;

using Microsoft.Data.SqlClient;

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
    public async Task<ActionResult<List<CountryResponse>>> GetAll(CancellationToken ct)
    {
        var countries = await _db.Countries
            .OrderBy(c => c.Id)
            .Select(CountryMapper.ToResponseExpression)
            .ToListAsync(ct);

        return Ok(countries);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateCountryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(statusCode: 400, title: "Country name is required.");

        if (string.IsNullOrWhiteSpace(request.IsoCode))
            return Problem(statusCode: 400, title: "ISO code is required.");

        request.IsoCode = request.IsoCode.ToUpperInvariant();

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
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Problem(statusCode: 409, title: $"Country with ISO code '{request.IsoCode}' already exists.");
        }

        return CreatedAtAction(nameof(GetAll), null);
    }
}
