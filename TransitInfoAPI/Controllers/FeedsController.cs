using System.IO.Compression;
using System.Security.Cryptography;

using CsvHelper;
using CsvHelper.Configuration;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Geometries;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class FeedsController : ControllerBase
{
    private readonly FeedService _feedService;
    private readonly TransitDbContext _db;
    private readonly ILogger<FeedsController> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IWebHostEnvironment _env;
    private readonly GtfsParserService _gtfs;

    public FeedsController(
        FeedService feedService,
        TransitDbContext db,
        ILogger<FeedsController> logger,
        IHttpClientFactory httpFactory,
        IWebHostEnvironment env,
        GtfsParserService gtfs)
    {
        _feedService = feedService;
        _db = db;
        _logger = logger;
        _httpFactory = httpFactory;
        _env = env;
        _gtfs = gtfs;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<FeedDto>>>> GetAll(
        [FromQuery] int after = 0,
        [FromQuery] int perPage = 50,
        CancellationToken ct = default)
    {
        var feeds = await _feedService.GetAllAsync(after, perPage, ct);
        var nextAfter = feeds.Count > 0 ? feeds.Last().Id : after;
        var total = await _db.Feeds.CountAsync(ct);
        var nextUrl = feeds.Count >= perPage ? $"{Request.Path}?after={nextAfter}&perPage={perPage}" : null;
        return Ok(OperationResult<List<FeedDto>>.OkPaginated(feeds, nextAfter, total, nextUrl));
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<FeedDto>>> Create(
        [FromQuery] int operatorId,
        [FromQuery] FeedType feedType,
        [FromQuery] SourceType sourceType,
        [FromQuery] string feedId,
        [FromQuery] string? externalUrl,
        [FromQuery] int refreshIntervalSeconds = 3600,
        CancellationToken ct = default)
    {
        var feed = await _feedService.CreateAsync(operatorId, feedType, sourceType, feedId, externalUrl, refreshIntervalSeconds, ct);
        var dto = await _feedService.GetByIdAsync(feed.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { }, OperationResult<FeedDto>.Ok(dto!));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult>> Update(int id, [FromBody] Feed updated, CancellationToken ct = default)
    {
        var (success, message) = await _feedService.UpdateAsync(id, updated, ct);
        if (!success) return NotFound(OperationResult.Fail(message!));
        return Ok(OperationResult.Ok("Feed updated."));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(int id, CancellationToken ct = default)
    {
        var success = await _feedService.DeleteAsync(id, ct);
        if (!success) return NotFound(OperationResult.Fail("Feed not found."));
        return Ok(OperationResult.Ok("Feed deleted."));
    }

    [HttpPost("{id}/fetch")]
    public async Task<ActionResult<OperationResult>> Fetch(int id)
    {
        try
        {
            var feed = await _db.Feeds.FindAsync([id]);
            if (feed is null) return NotFound(OperationResult.Fail("Feed not found."));

            var url = feed.ExternalUrl ?? feed.InternalUrl;
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest(OperationResult.Fail("Feed has no URL configured."));

            // Download
            var http = _httpFactory.CreateClient();
            var bytes = await http.GetByteArrayAsync(url);

            var dir = Path.Combine(_env.ContentRootPath, "feeds", feed.FeedId);
            Directory.CreateDirectory(dir);
            var zipPath = Path.Combine(dir, "gtfs.zip");
            await System.IO.File.WriteAllBytesAsync(zipPath, bytes);

            // Parse
            var agencies = _gtfs.ParseAgencies(zipPath);
            var rawStops = _gtfs.ParseStops(zipPath);
            var routes = _gtfs.ParseRoutes(zipPath);
            var trips = _gtfs.ParseTrips(zipPath);
            var calendar = _gtfs.ParseCalendar(zipPath);
            var calendarDates = _gtfs.ParseCalendarDates(zipPath);
            var shapes = _gtfs.ParseShapes(zipPath);

            var allStopTimes = new List<RawStopTimeRecord>();
            await foreach (var batch in _gtfs.ParseStopTimesBatchedAsync(zipPath))
                allStopTimes.AddRange(batch);

            var routeTypesPerStop = _gtfs.DeriveRouteTypesPerStop(routes, trips, allStopTimes);

            // Create FeedVersion
            var version = new FeedVersion
            {
                FeedId = id,
                Sha1 = _gtfs.ComputeGtfsSha1(zipPath),
                FetchedAt = DateTime.UtcNow,
                ImportStatus = FeedImportStatus.Importing,
                IsActive = false
            };
            _db.FeedVersions.Add(version);
            await _db.SaveChangesAsync();

            // Phase 1: agencies, stops, routes
            foreach (var a in agencies)
                _db.Agencies.Add(new Agency { FeedVersionId = version.Id, AgencyId = a.AgencyId, Name = a.AgencyName, Url = a.AgencyUrl, Timezone = a.AgencyTimezone, Language = a.AgencyLang, Phone = a.AgencyPhone, FareUrl = a.AgencyFareUrl, Email = a.AgencyEmail, OperatorId = feed.OperatorId });

            foreach (var s in rawStops)
            {
                routeTypesPerStop.TryGetValue(s.StopId, out var rt);
                _db.RawStops.Add(new RawStop { FeedVersionId = version.Id, RawStopId = s.StopId, Name = s.StopName, Lat = s.StopLat, Lon = s.StopLon, StationType = s.LocationType switch { 1 => StationType.Station, 2 => StationType.Platform, _ => StationType.Stop }, ParentRawStopId = s.ParentStation, StopCode = s.StopCode, StopDesc = s.StopDesc, ZoneId = s.ZoneId, PlatformCode = s.PlatformCode, WheelchairBoarding = s.WheelchairBoarding.HasValue ? s.WheelchairBoarding == 1 : null, RouteType = rt, IsActive = true, ReconciliationStatus = ReconciliationStatus.Pending });
            }

            foreach (var r in routes)
                _db.CanonicalRoutes.Add(new CanonicalRoute { GlobalId = $"gt-{feed.FeedId}-{r.RouteId.ToLowerInvariant()}", OnestopId = $"r-{feed.FeedId}-{r.RouteId.ToLowerInvariant()}", ShortName = r.RouteShortName, LongName = r.RouteLongName, RouteType = r.RouteTypeEnum, Color = r.RouteColor, TextColor = r.RouteTextColor, IsActive = true, OperatorId = feed.OperatorId });

            await _db.SaveChangesAsync();

            // Phase 2: trips, shapes, calendar
            foreach (var t in trips)
                _db.Trips.Add(new Trip { FeedVersionId = version.Id, TripId = t.TripId, RouteId = t.RouteId, ServiceId = t.ServiceId, TripHeadsign = t.TripHeadsign, TripShortName = t.TripShortName, DirectionId = t.DirectionId, ShapeId = t.ShapeId, WheelchairAccessible = t.WheelchairAccessible.HasValue ? t.WheelchairAccessible == 1 : null, BikesAllowed = t.BikesAllowed.HasValue ? t.BikesAllowed == 1 : null });

            foreach (var kvp in shapes)
                _db.Shapes.Add(new Shape { FeedVersionId = version.Id, ShapeId = kvp.Key, Geometry = kvp.Value });

            foreach (var c in calendar)
                _db.Calendars.Add(new Calendar { FeedVersionId = version.Id, ServiceId = c.ServiceId, Monday = c.Monday == 1, Tuesday = c.Tuesday == 1, Wednesday = c.Wednesday == 1, Thursday = c.Thursday == 1, Friday = c.Friday == 1, Saturday = c.Saturday == 1, Sunday = c.Sunday == 1, StartDate = DateOnly.ParseExact(c.StartDate, "yyyyMMdd"), EndDate = DateOnly.ParseExact(c.EndDate, "yyyyMMdd") });

            foreach (var cd in calendarDates)
                _db.CalendarDates.Add(new CalendarDate { FeedVersionId = version.Id, ServiceId = cd.ServiceId, Date = DateOnly.ParseExact(cd.Date, "yyyyMMdd"), ExceptionType = cd.ExceptionType });

            version.StopCount = rawStops.Count;
            version.RouteCount = routes.Count;
            version.TripCount = trips.Count;
            version.AgencyCount = agencies.Count;
            version.ConvexHull = _gtfs.ComputeConvexHull(rawStops);

            await _db.SaveChangesAsync();

            // Phase 3: stop times (need trip lookup, so trips must be saved first)
            var tripLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in await _db.Trips.Where(t => t.FeedVersionId == version.Id).ToListAsync())
                tripLookup.TryAdd(t.TripId, t.Id);

            for (var i = 0; i < allStopTimes.Count; i += 1000)
            {
                var batch = allStopTimes.Skip(i).Take(1000).ToList();
                foreach (var st in batch)
                    _db.StopTimes.Add(new StopTime { TripId = tripLookup.GetValueOrDefault(st.TripId, 0), RawStopId = st.StopId, ArrivalTime = GtfsParserService.ParseGtfsTimeToSeconds(st.ArrivalTime), DepartureTime = GtfsParserService.ParseGtfsTimeToSeconds(st.DepartureTime), StopSequence = st.StopSequence, StopHeadsign = st.StopHeadsign, PickupType = st.PickupType, DropOffType = st.DropOffType, Timepoint = st.Timepoint.HasValue ? st.Timepoint == 1 : null });

                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                _db.Entry(version).State = EntityState.Modified;
            }

            // Mark success
            version.IsActive = true;
            version.ImportStatus = FeedImportStatus.Success;
            version.ImportedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(OperationResult.Ok($"Import succeeded: {routes.Count} routes, {rawStops.Count} stops, {trips.Count} trips, {allStopTimes.Count} stop times."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch/import failed for feed {Id}", id);
            return StatusCode(500, OperationResult.Fail($"Import failed: {ex.Message}"));
        }
    }

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<OperationResult<List<FeedVersionDto>>>> GetVersions(int id, CancellationToken ct = default)
    {
        var versions = await _feedService.GetFeedVersionsAsync(id, ct);
        var dtos = versions.Select(v => new FeedVersionDto
        {
            Id = v.Id,
            FeedId = v.FeedId,
            Sha1 = v.Sha1,
            FetchedAt = v.FetchedAt,
            ImportedAt = v.ImportedAt,
            IsActive = v.IsActive,
            ImportStatus = v.ImportStatus.ToString(),
            ImportError = v.ImportError,
            ServiceLevelStart = v.ServiceLevelStart,
            ServiceLevelEnd = v.ServiceLevelEnd,
            StopCount = v.StopCount,
            RouteCount = v.RouteCount,
            TripCount = v.TripCount
        }).ToList();
        return Ok(OperationResult<List<FeedVersionDto>>.Ok(dtos));
    }
}
