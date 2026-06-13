using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("realtime")]
public class RealtimeController : ControllerBase
{
    private readonly TransitDbContext _db;

    public RealtimeController(TransitDbContext db) { _db = db; }

    [HttpGet("vehicles")]
    public async Task<ActionResult<List<object>>> GetVehicles(
        [FromQuery] string? operatorGlobalId,
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] double? radiusKm,
        CancellationToken ct = default)
    {
        // TODO: Query OTP for real-time vehicle positions
        return new List<object>();
    }
}
