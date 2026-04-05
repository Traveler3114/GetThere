using GetThereAPI.Data;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Controllers;

/// <summary>
/// Exposes country lookup data used for the country selector in the app.
///
/// GET /countries → list of all available countries { Id, Name }
/// </summary>
[ApiController]
[Route("[controller]")]
public class CountryController : ControllerBase
{
    private readonly AppDbContext _db;

    public CountryController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns all countries available in the system.</summary>
    // GET /countries
    [HttpGet]
    public async Task<ActionResult<OperationResult<List<CountryDto>>>> GetAll()
    {
        var countries = await _db.Countries
            .OrderBy(c => c.Name)
            .Select(c => new CountryDto { Id = c.Id, Name = c.Name })
            .ToListAsync();

        return Ok(OperationResult<List<CountryDto>>.Ok(countries));
    }
}
