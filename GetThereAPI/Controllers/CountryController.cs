using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Mapping;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

/// <summary>
/// Exposes country lookup data used for the country selector in the app.
///
/// GET /countries → list of all available countries { Id, Name }
/// </summary>
[ApiController]
[Route("countries")]
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
    public async Task<ActionResult<OperationResult<List<CountryResponse>>>> GetAll(CancellationToken ct = default)
    {
        var countries = await _db.Countries
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return Ok(OperationResult<List<CountryResponse>>.Ok(countries.Select(CountryMapper.ToResponse).ToList()));
    }
}
