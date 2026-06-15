using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

using NetTopologySuite.Geometries;

namespace TransitInfoAPI.Services;

public class FeedService
{
    private readonly TransitDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FeedService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly GtfsParserService _gtfs;
    private readonly OnestopIdService _onestopId;
    private readonly ReconciliationService _reconciliation;

    public FeedService(
        TransitDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<FeedService> logger,
        IWebHostEnvironment env,
        GtfsParserService gtfs,
        OnestopIdService onestopId,
        ReconciliationService reconciliation)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
        _env = env;
        _gtfs = gtfs;
        _onestopId = onestopId;
        _reconciliation = reconciliation;
    }

    public async Task<List<FeedDto>> GetAllAsync(int after = 0, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.Feeds
            .Include(f => f.Operator)
            .OrderBy(f => f.Id)
            .AsQueryable();

        if (after > 0)
            query = query.Where(f => f.Id > after);

        if (perPage > 0)
            query = query.Take(perPage);

        return await query
            .Select(f => new FeedDto
            {
                Id = f.Id,
                OnestopId = f.OnestopId,
                FeedType = f.FeedType.ToString(),
                FeedId = f.FeedId,
                ExternalUrl = f.ExternalUrl,
                InternalUrl = f.InternalUrl,
                IsActive = f.IsActive,
                RefreshIntervalSeconds = f.RefreshIntervalSeconds,
                OperatorName = f.Operator.Name
            })
            .ToListAsync(ct);
    }

    public async Task<FeedDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.Feeds
            .Include(f => f.Operator)
            .Where(f => f.Id == id)
            .Select(f => new FeedDto
            {
                Id = f.Id,
                OnestopId = f.OnestopId,
                FeedType = f.FeedType.ToString(),
                FeedId = f.FeedId,
                ExternalUrl = f.ExternalUrl,
                InternalUrl = f.InternalUrl,
                IsActive = f.IsActive,
                RefreshIntervalSeconds = f.RefreshIntervalSeconds,
                OperatorName = f.Operator.Name
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Feed> CreateAsync(
        int operatorId, FeedType feedType, SourceType sourceType,
        string feedId, string? externalUrl, int refreshIntervalSeconds, CancellationToken ct)
    {
        var op = await _db.Operators.FindAsync([operatorId], ct)
            ?? throw new InvalidOperationException("Operator not found.");

        var onestopId = _onestopId.GenerateFeedOnestopId(0, 0, feedId);

        var feed = new Feed
        {
            OnestopId = onestopId,
            OperatorId = operatorId,
            FeedType = feedType,
            SourceType = sourceType,
            FeedId = feedId,
            ExternalUrl = externalUrl,
            RefreshIntervalSeconds = refreshIntervalSeconds,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Feeds.Add(feed);
        await _db.SaveChangesAsync(ct);
        return feed;
    }

    public async Task<(bool Success, string? Message)> UpdateAsync(int id, Feed updated, CancellationToken ct)
    {
        var feed = await _db.Feeds.FindAsync([id], ct);
        if (feed is null) return (false, "Feed not found.");

        feed.FeedType = updated.FeedType;
        feed.ExternalUrl = updated.ExternalUrl;
        feed.InternalUrl = updated.InternalUrl;
        feed.IsActive = updated.IsActive;
        feed.RefreshIntervalSeconds = updated.RefreshIntervalSeconds;
        feed.LicenseName = updated.LicenseName;
        feed.LicenseUrl = updated.LicenseUrl;
        feed.LicenseCommercialUseAllowed = updated.LicenseCommercialUseAllowed;
        feed.LicenseShareAlikeOptional = updated.LicenseShareAlikeOptional;
        feed.LicenseRedistributionAllowed = updated.LicenseRedistributionAllowed;

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var feed = await _db.Feeds
            .Include(f => f.FeedVersions)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feed is null) return false;

        var candidates = await _db.ReconciliationCandidates
            .Where(rc => rc.FeedId == id)
            .ToListAsync(ct);
        _db.ReconciliationCandidates.RemoveRange(candidates);

        var versions = await _db.FeedVersions
            .Where(fv => fv.FeedId == id)
            .ToListAsync(ct);
        _db.FeedVersions.RemoveRange(versions);

        _db.Feeds.Remove(feed);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FeedVersion?> CheckAndFetchAsync(int feedId, CancellationToken ct)
    {
        var feed = await _db.Feeds.FindAsync([feedId], ct);
        if (feed is null) return null;

        var url = feed.ExternalUrl ?? feed.InternalUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var http = _httpFactory.CreateClient();
        var bytes = await http.GetByteArrayAsync(url, ct);

        var outputDir = Path.Combine(_env.ContentRootPath, "feeds", feed.FeedId);
        Directory.CreateDirectory(outputDir);
        var zipPath = Path.Combine(outputDir, "gtfs.zip");
        var tmpPath = zipPath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, bytes, ct);
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        File.Move(tmpPath, zipPath);

        var sha1 = _gtfs.ComputeGtfsSha1(zipPath);

        var existing = await _db.FeedVersions
            .Where(fv => fv.FeedId == feedId && fv.Sha1 == sha1)
            .AnyAsync(ct);

        if (existing)
        {
            _logger.LogInformation("Feed {FeedId} SHA1 unchanged ({Sha1}), skipping", feed.FeedId, sha1);
            var existingVersion = await _db.FeedVersions
                .Where(fv => fv.FeedId == feedId && fv.Sha1 == sha1)
                .OrderByDescending(fv => fv.FetchedAt)
                .FirstOrDefaultAsync(ct);
            return existingVersion;
        }

        var version = new FeedVersion
        {
            FeedId = feedId,
            Sha1 = sha1,
            FetchedAt = DateTime.UtcNow,
            ImportStatus = FeedImportStatus.Pending,
            IsActive = false
        };

        _db.FeedVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("New FeedVersion {VersionId} for feed {FeedId} SHA1={Sha1}", version.Id, feed.FeedId, sha1);
        return version;
    }

    public async Task ImportFeedVersionAsync(int feedVersionId, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Include(fv => fv.Feed)
                .ThenInclude(f => f.Operator)
            .FirstOrDefaultAsync(fv => fv.Id == feedVersionId, CancellationToken.None);

        if (version is null) throw new InvalidOperationException("FeedVersion not found.");

        var zipPath = Path.Combine(_env.ContentRootPath, "feeds", version.Feed.FeedId, "gtfs.zip");
        if (!File.Exists(zipPath))
        {
            version.ImportStatus = FeedImportStatus.Failed;
            version.ImportError = "GTFS zip not found on disk";
            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        version.ImportStatus = FeedImportStatus.Importing;
        await _db.SaveChangesAsync(CancellationToken.None);

        try
        {
            _db.Database.SetCommandTimeout(180);
            var validation = _gtfs.ValidateGtfs(zipPath);
            if (!validation.IsValid)
            {
                version.ImportStatus = FeedImportStatus.Failed;
                version.ImportError = "GTFS validation failed: " + string.Join("; ", validation.Errors);
                    await _db.SaveChangesAsync(CancellationToken.None);
                    return;
                }

                var agencies = _gtfs.ParseAgencies(zipPath);
            var rawStops = _gtfs.ParseStops(zipPath);
            var routes = _gtfs.ParseRoutes(zipPath);
            var trips = _gtfs.ParseTrips(zipPath);
            var calendar = _gtfs.ParseCalendar(zipPath);
            var calendarDates = _gtfs.ParseCalendarDates(zipPath);
            var shapes = _gtfs.ParseShapes(zipPath);

            var operatorId = version.Feed.OperatorId;

            foreach (var a in agencies)
            {
                _db.Agencies.Add(new Agency
                {
                    FeedVersionId = feedVersionId,
                    AgencyId = a.AgencyId,
                    Name = a.AgencyName,
                    Url = a.AgencyUrl,
                    Timezone = a.AgencyTimezone,
                    Language = a.AgencyLang,
                    Phone = a.AgencyPhone,
                    FareUrl = a.AgencyFareUrl,
                    Email = a.AgencyEmail,
                    OperatorId = operatorId
                });
            }

            var allStopTimes = new List<RawStopTimeRecord>();
            await foreach (var batch in _gtfs.ParseStopTimesBatchedAsync(zipPath))
                allStopTimes.AddRange(batch);

            var routeTypesPerStop = _gtfs.DeriveRouteTypesPerStop(routes, trips, allStopTimes);

            foreach (var s in rawStops)
            {
                routeTypesPerStop.TryGetValue(s.StopId, out var rt);
                _db.RawStops.Add(new RawStop
                {
                    FeedVersionId = feedVersionId,
                    RawStopId = s.StopId,
                    Name = s.StopName,
                    Lat = s.StopLat,
                    Lon = s.StopLon,
                    StationType = MapGtfsLocationType(s.LocationType),
                    ParentRawStopId = s.ParentStation,
                    StopCode = s.StopCode,
                    StopDesc = s.StopDesc,
                    ZoneId = s.ZoneId,
                    PlatformCode = s.PlatformCode,
                    WheelchairBoarding = s.WheelchairBoarding.HasValue ? s.WheelchairBoarding == 1 : null,
                    RouteType = rt,
                    IsActive = true,
                    ReconciliationStatus = ReconciliationStatus.Pending
                });
            }

            foreach (var r in routes)
            {
                var existingRoute = await _db.CanonicalRoutes
                    .FirstOrDefaultAsync(cr =>
                        cr.GlobalId == $"gt-{version.Feed.FeedId}-{r.RouteId.ToLowerInvariant()}", CancellationToken.None);

                if (existingRoute is null)
                {
                    var routeOnestopId = _onestopId.GenerateRouteOnestopId(
                        rawStops.Count > 0 ? rawStops.Average(s => s.StopLat) : 0,
                        rawStops.Count > 0 ? rawStops.Average(s => s.StopLon) : 0,
                        r.RouteShortName);

                    _db.CanonicalRoutes.Add(new CanonicalRoute
                    {
                        GlobalId = $"gt-{version.Feed.FeedId}-{r.RouteId.ToLowerInvariant()}",
                        OnestopId = routeOnestopId,
                        ShortName = r.RouteShortName,
                        LongName = r.RouteLongName,
                        RouteType = r.RouteTypeEnum,
                        Color = r.RouteColor,
                        TextColor = r.RouteTextColor,
                        IsActive = true,
                        OperatorId = operatorId
                    });
                }
            }

            var prefix = $"gt-{version.Feed.FeedId}-";
            var canonicalRouteLookup = await _db.CanonicalRoutes
                .Where(cr => cr.GlobalId.StartsWith(prefix))
                .ToDictionaryAsync(cr => cr.GlobalId, cr => cr.Id, StringComparer.OrdinalIgnoreCase, CancellationToken.None);

            foreach (var t in trips)
            {
                var globalId = $"gt-{version.Feed.FeedId}-{t.RouteId.ToLowerInvariant()}";

                _db.Trips.Add(new Trip
                {
                    FeedVersionId = feedVersionId,
                    TripId = t.TripId,
                    RouteId = t.RouteId,
                    ServiceId = t.ServiceId,
                    TripHeadsign = t.TripHeadsign,
                    TripShortName = t.TripShortName,
                    DirectionId = t.DirectionId,
                    ShapeId = t.ShapeId,
                    WheelchairAccessible = t.WheelchairAccessible.HasValue ? t.WheelchairAccessible == 1 : null,
                    BikesAllowed = t.BikesAllowed.HasValue ? t.BikesAllowed == 1 : null,
                    CanonicalRouteId = canonicalRouteLookup.GetValueOrDefault(globalId)
                });
            }

            foreach (var kvp in shapes)
            {
                _db.Shapes.Add(new Shape
                {
                    FeedVersionId = feedVersionId,
                    ShapeId = kvp.Key,
                    Geometry = kvp.Value
                });
            }

            foreach (var c in calendar)
            {
                _db.Calendars.Add(new Calendar
                {
                    FeedVersionId = feedVersionId,
                    ServiceId = c.ServiceId,
                    Monday = c.Monday == 1,
                    Tuesday = c.Tuesday == 1,
                    Wednesday = c.Wednesday == 1,
                    Thursday = c.Thursday == 1,
                    Friday = c.Friday == 1,
                    Saturday = c.Saturday == 1,
                    Sunday = c.Sunday == 1,
                    StartDate = DateOnly.ParseExact(c.StartDate, "yyyyMMdd"),
                    EndDate = DateOnly.ParseExact(c.EndDate, "yyyyMMdd")
                });
            }

            foreach (var cd in calendarDates)
            {
                _db.CalendarDates.Add(new CalendarDate
                {
                    FeedVersionId = feedVersionId,
                    ServiceId = cd.ServiceId,
                    Date = DateOnly.ParseExact(cd.Date, "yyyyMMdd"),
                    ExceptionType = cd.ExceptionType
                });
            }

            var convexHull = _gtfs.ComputeConvexHull(rawStops);

            version.StopCount = rawStops.Count;
            version.RouteCount = routes.Count;
            version.TripCount = trips.Count;
            version.AgencyCount = agencies.Count;
            version.ConvexHull = convexHull;

            if (calendar.Count > 0)
            {
                version.ServiceLevelStart = calendar.Min(c => DateOnly.ParseExact(c.StartDate, "yyyyMMdd"));
                version.ServiceLevelEnd = calendar.Max(c => DateOnly.ParseExact(c.EndDate, "yyyyMMdd"));
            }

            await _db.SaveChangesAsync(CancellationToken.None);

            var tripLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in await _db.Trips
                .Where(t => t.FeedVersionId == feedVersionId)
                .ToListAsync(CancellationToken.None))
            {
                tripLookup.TryAdd(t.TripId, t.Id);
            }

            for (var i = 0; i < allStopTimes.Count; i += 1000)
            {
                var batch = allStopTimes.Skip(i).Take(1000).ToList();
                _db.StopTimes.AddRange(batch.Select(st => new StopTime
                {
                    TripId = tripLookup.GetValueOrDefault(st.TripId, 0),
                    RawStopId = st.StopId,
                    ArrivalTime = GtfsParserService.ParseGtfsTimeToSeconds(st.ArrivalTime),
                    DepartureTime = GtfsParserService.ParseGtfsTimeToSeconds(st.DepartureTime),
                    StopSequence = st.StopSequence,
                    StopHeadsign = st.StopHeadsign,
                    PickupType = st.PickupType,
                    DropOffType = st.DropOffType,
                    Timepoint = st.Timepoint.HasValue ? st.Timepoint == 1 : null
                }));

                await _db.SaveChangesAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
            }

            _db.Entry(version).State = EntityState.Modified;

            var prevActive = await _db.FeedVersions
                .Where(fv => fv.FeedId == version.FeedId && fv.IsActive && fv.Id != feedVersionId)
                .ToListAsync(CancellationToken.None);
            foreach (var pv in prevActive)
                pv.IsActive = false;

            version.IsActive = true;
            version.ImportStatus = FeedImportStatus.Success;
            version.ImportedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation("Import complete for FeedVersion {VersionId} ({RouteCount} routes, {StopCount} stops, {TripCount} trips)",
                feedVersionId, routes.Count, rawStops.Count, trips.Count);
        }
        catch (Exception ex)
        {
            _db.Entry(version).State = EntityState.Modified;
            version.ImportStatus = FeedImportStatus.Failed;
            version.ImportError = ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Import failed for FeedVersion {VersionId}", feedVersionId);
        }
    }

    public async Task<FeedVersion> TriggerImportAsync(int feedId, CancellationToken ct)
    {
        var version = await CheckAndFetchAsync(feedId, ct);
        if (version is null)
        {
            var feed = await _db.Feeds.FindAsync([feedId], ct);
            if (feed is null) throw new InvalidOperationException("Feed not found.");

            var zipPath = Path.Combine(_env.ContentRootPath, "feeds", feed.FeedId, "gtfs.zip");
            var sha1 = File.Exists(zipPath) ? _gtfs.ComputeGtfsSha1(zipPath) : "manual-trigger";

            version = new FeedVersion
            {
                FeedId = feedId,
                Sha1 = sha1,
                FetchedAt = DateTime.UtcNow,
                ImportStatus = FeedImportStatus.Pending,
                IsActive = false
            };
            _db.FeedVersions.Add(version);
            await _db.SaveChangesAsync(ct);
        }

        await ImportFeedVersionAsync(version.Id, CancellationToken.None);
        return version;
    }

    public async Task<List<FeedVersion>> GetFeedVersionsAsync(int feedId, CancellationToken ct)
    {
        return await _db.FeedVersions
            .Where(fv => fv.FeedId == feedId)
            .OrderByDescending(fv => fv.FetchedAt)
            .ToListAsync(ct);
    }

    public async Task<FeedVersion?> GetActiveFeedVersionAsync(int feedId, CancellationToken ct)
    {
        return await _db.FeedVersions
            .FirstOrDefaultAsync(fv => fv.FeedId == feedId && fv.IsActive, ct);
    }

    public async Task<List<Feed>> GetActiveGtfsFeedsAsync(CancellationToken ct)
    {
        return await _db.Feeds
            .Where(f => f.IsActive && f.FeedType == FeedType.GTFSStatic)
            .ToListAsync(ct);
    }

    private static StationType MapGtfsLocationType(int locationType) => locationType switch
    {
        0 => StationType.Stop,
        1 => StationType.Station,
        2 => StationType.Platform,
        3 => StationType.Stop,
        4 => StationType.Platform,
        _ => StationType.Stop
    };


}
