using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("feed-versions")]
public class FeedVersionsController : ControllerBase
{
    private readonly TransitDbContext _db;

    public FeedVersionsController(TransitDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<Paginated<FeedVersionDto>>> GetAll(
        [FromQuery] int? feedId = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.FeedVersions
            .OrderByDescending(fv => fv.FetchedAt)
            .AsQueryable();

        if (feedId.HasValue)
            query = query.Where(fv => fv.FeedId == feedId.Value);

        var total = await query.CountAsync(ct);
        var versions = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(fv => new FeedVersionDto
            {
                Id = fv.Id,
                FeedId = fv.FeedId,
                Sha1 = fv.Sha1,
                FetchedAt = fv.FetchedAt,
                ImportedAt = fv.ImportedAt,
                IsActive = fv.IsActive,
                ImportStatus = fv.ImportStatus.ToString(),
                ImportError = fv.ImportError,
                ServiceLevelStart = fv.ServiceLevelStart,
                ServiceLevelEnd = fv.ServiceLevelEnd,
                StopCount = fv.StopCount,
                RouteCount = fv.RouteCount,
                TripCount = fv.TripCount
            })
            .ToListAsync(ct);

        return Ok(new Paginated<FeedVersionDto>(versions, total, page, perPage));
    }

    [HttpGet("{sha1}")]
    public async Task<ActionResult<FeedVersionDto>> GetBySha1(string sha1, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Where(fv => fv.Sha1 == sha1)
            .Select(fv => new FeedVersionDto
            {
                Id = fv.Id,
                FeedId = fv.FeedId,
                Sha1 = fv.Sha1,
                FetchedAt = fv.FetchedAt,
                ImportedAt = fv.ImportedAt,
                IsActive = fv.IsActive,
                ImportStatus = fv.ImportStatus.ToString(),
                ImportError = fv.ImportError,
                ServiceLevelStart = fv.ServiceLevelStart,
                ServiceLevelEnd = fv.ServiceLevelEnd,
                StopCount = fv.StopCount,
                RouteCount = fv.RouteCount,
                TripCount = fv.TripCount
            })
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return NotFound();

        return Ok(version);
    }

    [HttpGet("{sha1}/stops")]
    public async Task<ActionResult<List<RawStopDto>>> GetStops(string sha1, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Where(fv => fv.Sha1 == sha1)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return NotFound();

        var stops = await _db.RawStops
            .Where(rs => rs.FeedVersionId == version.Id)
            .OrderBy(rs => rs.Id)
            .Select(rs => new RawStopDto
            {
                Id = rs.Id,
                RawStopId = rs.RawStopId,
                Name = rs.Name,
                Lat = rs.Lat,
                Lon = rs.Lon,
                StationType = rs.StationType.ToString(),
                RouteType = rs.RouteType.ToString(),
                CanonicalStationId = rs.CanonicalStationId,
                ReconciliationStatus = rs.ReconciliationStatus.ToString()
            })
            .ToListAsync(ct);

        return Ok(stops);
    }
}


