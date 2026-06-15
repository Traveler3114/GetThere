using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
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
    public async Task<ActionResult<OperationResult<List<FeedVersionDto>>>> GetAll(
        [FromQuery] int? feedId = null,
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var query = _db.FeedVersions
            .OrderByDescending(fv => fv.FetchedAt)
            .AsQueryable();

        if (feedId.HasValue)
            query = query.Where(fv => fv.FeedId == feedId.Value);

        if (after > 0)
            query = query.Where(fv => fv.Id < after);

        var versions = await query
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

        var nextAfter = versions.Count > 0 ? versions.Last().Id : after;
        var total = await _db.FeedVersions.CountAsync(ct);
        var nextUrl = versions.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<FeedVersionDto>>.OkPaginated(versions, nextAfter, total, nextUrl));
    }

    [HttpGet("{sha1}")]
    public async Task<ActionResult<OperationResult<FeedVersionDto>>> GetBySha1(string sha1, CancellationToken ct = default)
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
            return NotFound(OperationResult<FeedVersionDto>.Fail("Feed version not found."));

        return Ok(OperationResult<FeedVersionDto>.Ok(version));
    }

    [HttpGet("{sha1}/stops")]
    public async Task<ActionResult<OperationResult<List<RawStopDto>>>> GetStops(string sha1, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Where(fv => fv.Sha1 == sha1)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return NotFound(OperationResult<List<RawStopDto>>.Fail("Feed version not found."));

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

        return Ok(OperationResult<List<RawStopDto>>.Ok(stops));
    }
}

public class RawStopDto
{
    public int Id { get; set; }
    public string RawStopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string StationType { get; set; } = string.Empty;
    public string RouteType { get; set; } = string.Empty;
    public int? CanonicalStationId { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
}
