using System.Collections.Concurrent;
using System.Data;
using System.IO.Compression;

using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

using Microsoft.Data.SqlClient;

using NetTopologySuite.Geometries;

namespace TransitInfoAPI.Managers;

public class FeedManager
{
    private readonly TransitDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FeedManager> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly GtfsParserManager _gtfs;
    private readonly OnestopIdManager _onestopId;
    private readonly ReconciliationManager _reconciliation;
    private readonly PlaceMatchingManager _placeMatching;
    private readonly ImportLogStore _logStore;
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _feedLocks = new();

    public FeedManager(
        TransitDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<FeedManager> logger,
        IWebHostEnvironment env,
        GtfsParserManager gtfs,
        OnestopIdManager onestopId,
        ReconciliationManager reconciliation,
        PlaceMatchingManager placeMatching,
        ImportLogStore logStore)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
        _env = env;
        _gtfs = gtfs;
        _onestopId = onestopId;
        _reconciliation = reconciliation;
        _placeMatching = placeMatching;
        _logStore = logStore;
    }

    public async Task<List<FeedDto>> GetAllAsync(int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.Feeds
            .Include(f => f.Operator)
            .OrderBy(f => f.Id)
            .AsQueryable();

        return await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
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
                OperatorName = f.Operator.Name,
                LicenseName = f.LicenseName,
                LicenseUrl = f.LicenseUrl,
                LicenseCommercialUseAllowed = f.LicenseCommercialUseAllowed,
                LicenseShareAlikeOptional = f.LicenseShareAlikeOptional,
                LicenseRedistributionAllowed = f.LicenseRedistributionAllowed
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
                OperatorName = f.Operator.Name,
                LicenseName = f.LicenseName,
                LicenseUrl = f.LicenseUrl,
                LicenseCommercialUseAllowed = f.LicenseCommercialUseAllowed,
                LicenseShareAlikeOptional = f.LicenseShareAlikeOptional,
                LicenseRedistributionAllowed = f.LicenseRedistributionAllowed
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

    public async Task<(bool Success, string? Message)> UpdateAsync(int id, UpdateFeedRequest request, CancellationToken ct)
    {
        var feed = await _db.Feeds.FindAsync([id], ct);
        if (feed is null) return (false, "Feed not found.");

        if (!Enum.TryParse<FeedType>(request.FeedType, true, out var feedType))
            return (false, $"Invalid feed type '{request.FeedType}'.");
        feed.FeedType = feedType;
        feed.ExternalUrl = request.ExternalUrl;
        feed.InternalUrl = request.InternalUrl;
        feed.IsActive = request.IsActive;
        feed.RefreshIntervalSeconds = request.RefreshIntervalSeconds;
        feed.LicenseName = request.LicenseName;
        feed.LicenseUrl = request.LicenseUrl;
        feed.LicenseCommercialUseAllowed = request.LicenseCommercialUseAllowed;
        feed.LicenseShareAlikeOptional = request.LicenseShareAlikeOptional;
        feed.LicenseRedistributionAllowed = request.LicenseRedistributionAllowed;

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var feed = await _db.Feeds
            .Include(f => f.FeedVersions)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feed is null) return false;

        var versionIds = await _db.FeedVersions
            .Where(fv => fv.FeedId == id)
            .Select(fv => fv.Id)
            .ToListAsync(ct);

        if (versionIds.Count > 0)
        {
            foreach (var vid in versionIds)
            {
                await _db.StopTimes.Where(st => st.Trip.FeedVersionId == vid).ExecuteDeleteAsync(ct);
                await _db.CalendarDates.Where(cd => cd.FeedVersionId == vid).ExecuteDeleteAsync(ct);
                await _db.Calendars.Where(c => c.FeedVersionId == vid).ExecuteDeleteAsync(ct);
                await _db.Shapes.Where(s => s.FeedVersionId == vid).ExecuteDeleteAsync(ct);
                await _db.Trips.Where(t => t.FeedVersionId == vid).ExecuteDeleteAsync(ct);
                await _db.RawStops.Where(rs => rs.FeedVersionId == vid).ExecuteDeleteAsync(ct);
                await _db.Agencies.Where(a => a.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            }
        }

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
        var sem = _feedLocks.GetOrAdd(feedId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
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
            try
            {
                File.Replace(tmpPath, zipPath, null);
            }
            catch (FileNotFoundException)
            {
                File.Move(tmpPath, zipPath);
            }

            string sha1;
            using (var archive = ZipFile.OpenRead(zipPath))
                sha1 = _gtfs.ComputeGtfsSha1(archive);

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
                if (existingVersion is not null)
                    _logStore.AddEntry(existingVersion.Id, $"SHA1 unchanged ({sha1}), skipping import");
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
            _logStore.AddEntry(version.Id, $"Downloaded {bytes.Length:N0} bytes from {url}");
            _logStore.AddEntry(version.Id, $"SHA1 = {sha1}");
            return version;
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task ImportFeedVersionAsync(int feedVersionId, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Include(fv => fv.Feed)
                .ThenInclude(f => f.Operator)
            .FirstOrDefaultAsync(fv => fv.Id == feedVersionId, ct);

        if (version is null) throw new InvalidOperationException("FeedVersion not found.");

        var zipPath = Path.Combine(_env.ContentRootPath, "feeds", version.Feed.FeedId, "gtfs.zip");
        if (!File.Exists(zipPath))
        {
            version.ImportStatus = FeedImportStatus.Failed;
            version.ImportError = "GTFS zip not found on disk";
            _logStore.AddEntry(feedVersionId, "Error: GTFS zip not found on disk");
            await _db.SaveChangesAsync(ct);
            return;
        }

        version.ImportStatus = FeedImportStatus.Importing;
        await _db.SaveChangesAsync(ct);

        _logStore.AddEntry(feedVersionId, "Import started");

        var importTempDir = Path.Combine(Path.GetTempPath(), "gtfs-imports");
        Directory.CreateDirectory(importTempDir);
        var tempZipPath = Path.Combine(importTempDir, $"import-{feedVersionId}-{Guid.NewGuid()}.zip");
        File.Copy(zipPath, tempZipPath, overwrite: true);
        _logStore.AddEntry(feedVersionId, "Copied GTFS zip to temporary working file");

        using var archive = ZipFile.OpenRead(tempZipPath);
        _logStore.AddEntry(feedVersionId, "Opening GTFS archive...");

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
        var sqlConn = (Microsoft.Data.SqlClient.SqlConnection)conn;
        var sqlTx = sqlConn.BeginTransaction();
        _db.Database.UseTransaction(sqlTx);
        try
        {
            _db.Database.SetCommandTimeout(600);
            _db.ChangeTracker.AutoDetectChangesEnabled = false;
            _logStore.AddEntry(feedVersionId, "Validating GTFS files...");
            var validation = _gtfs.ValidateGtfs(archive);
            if (!validation.IsValid)
            {
                version.ImportStatus = FeedImportStatus.Failed;
                version.ImportError = "GTFS validation failed: " + string.Join("; ", validation.Errors);
                _logStore.AddEntry(feedVersionId, $"Validation failed: {string.Join("; ", validation.Errors)}");
            await _db.SaveChangesAsync(ct);
            await sqlTx.CommitAsync(ct);
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
            return;
            }

            _logStore.AddEntry(feedVersionId, "Cleaning up existing data for this feed version...");
            await _db.Database.ExecuteSqlRawAsync("WHILE 1=1 BEGIN DELETE TOP (50000) StopTimes WHERE TripId IN (SELECT Id FROM Trips WHERE FeedVersionId = @p0); IF @@ROWCOUNT = 0 BREAK END", new object[] { feedVersionId }, ct);
            await _db.CalendarDates.Where(cd => cd.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
            await _db.Calendars.Where(c => c.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
            await _db.Shapes.Where(s => s.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
            await _db.Trips.Where(t => t.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
            await _db.ReconciliationCandidates.Where(rc => rc.RawStop.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
            await _db.RawStops.Where(rs => rs.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
            await _db.Agencies.Where(a => a.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
            _logStore.AddEntry(feedVersionId, "Cleanup complete");

            _logStore.AddEntry(feedVersionId, "Validation passed");
            _logStore.AddEntry(feedVersionId, "Parsing GTFS files...");
            var agencies = _gtfs.ParseAgencies(archive);
            var (rawStops, droppedStops) = _gtfs.ParseStops(archive);
            if (droppedStops > 0)
                _logStore.AddEntry(feedVersionId, $"Skipped {droppedStops} stop(s) with invalid coordinates");
            var routes = _gtfs.ParseRoutes(archive);
            var trips = _gtfs.ParseTrips(archive);
            var calendar = _gtfs.ParseCalendar(archive);
            var calendarDates = _gtfs.ParseCalendarDates(archive);
            var shapes = _gtfs.ParseShapes(archive);
            _logStore.AddEntry(feedVersionId, $"Parsed: {agencies.Count} agencies, {rawStops.Count} stops, {routes.Count} routes, {trips.Count} trips, {calendar.Count} calendars, {calendarDates.Count} calendar_dates, {shapes.Count} shapes");

            var operatorId = version.Feed.OperatorId;

            var routeTypeByRoute = routes
                .GroupBy(r => r.RouteId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().RouteTypeEnum, StringComparer.OrdinalIgnoreCase);
            var routeByTrip = trips
                .GroupBy(t => t.TripId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().RouteId, StringComparer.OrdinalIgnoreCase);

            // Phase 1: save routes → canonical IDs
            _logStore.AddEntry(feedVersionId, "Phase 1: Saving canonical routes...");
            var seenOnestopIds = new HashSet<string>();
            foreach (var r in routes)
            {
                var globalId = $"gt-{version.Feed.FeedId}-{r.RouteId.ToLowerInvariant()}";
                var existingRoute = await _db.CanonicalRoutes
                    .FirstOrDefaultAsync(cr => cr.GlobalId == globalId, ct);

                if (existingRoute is null)
                {
                    var routeName = r.RouteShortName;
                    if (string.IsNullOrEmpty(routeName))
                        routeName = r.RouteLongName;
                    if (string.IsNullOrEmpty(routeName))
                        routeName = r.RouteId;
                    if (string.IsNullOrEmpty(routeName))
                        routeName = "unknown";

                    var routeOnestopId = _onestopId.GenerateRouteOnestopId(
                        rawStops.Count > 0 ? rawStops.Average(s => s.StopLat) : 0,
                        rawStops.Count > 0 ? rawStops.Average(s => s.StopLon) : 0,
                        routeName);

                    var existingByOnestop = await _db.CanonicalRoutes
                        .FirstOrDefaultAsync(cr => cr.OnestopId == routeOnestopId, ct);
                    if (existingByOnestop is not null || !seenOnestopIds.Add(routeOnestopId))
                        continue;

                    _db.CanonicalRoutes.Add(new CanonicalRoute
                    {
                        GlobalId = globalId,
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

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);
            _logStore.AddEntry(feedVersionId, $"Phase 1: {routes.Count} routes saved");

            var prefix = $"gt-{version.Feed.FeedId}-";
            var canonicalRouteLookup = await _db.CanonicalRoutes
                .Where(cr => cr.GlobalId.StartsWith(prefix))
                .ToDictionaryAsync(cr => cr.GlobalId, cr => cr.Id, StringComparer.OrdinalIgnoreCase, ct);

            // Phase 2: save trips, shapes, calendars → trip IDs
            _logStore.AddEntry(feedVersionId, $"Phase 2: Saving {trips.Count} trips, {shapes.Count} shapes, {calendar.Count} calendars...");
            foreach (var t in trips)
            {
                var globalId = prefix + t.RouteId.ToLowerInvariant();
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
                    WheelchairAccessible = t.WheelchairAccessible switch
                    {
                        1 => true,
                        2 => false,
                        _ => null
                    },
                    BikesAllowed = t.BikesAllowed.HasValue ? t.BikesAllowed == 1 : null,
                    CanonicalRouteId = canonicalRouteLookup.GetValueOrDefault(globalId)
                });
            }

            foreach (var kvp in shapes)
                _db.Shapes.Add(new Shape { FeedVersionId = feedVersionId, ShapeId = kvp.Key, Geometry = kvp.Value });

            foreach (var c in calendar)
                _db.Calendars.Add(new Calendar
                {
                    FeedVersionId = feedVersionId,
                    ServiceId = c.ServiceId,
                    Monday = c.Monday == 1, Tuesday = c.Tuesday == 1, Wednesday = c.Wednesday == 1,
                    Thursday = c.Thursday == 1, Friday = c.Friday == 1, Saturday = c.Saturday == 1,
                    Sunday = c.Sunday == 1,
                    StartDate = DateOnly.ParseExact(c.StartDate, "yyyyMMdd"),
                    EndDate = DateOnly.ParseExact(c.EndDate, "yyyyMMdd")
                });

            foreach (var cd in calendarDates)
                _db.CalendarDates.Add(new CalendarDate
                {
                    FeedVersionId = feedVersionId,
                    ServiceId = cd.ServiceId,
                    Date = DateOnly.ParseExact(cd.Date, "yyyyMMdd"),
                    ExceptionType = cd.ExceptionType
                });

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);
            _logStore.AddEntry(feedVersionId, "Phase 2: trips, shapes, calendars saved");

            // Build trip lookup
            var tripLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in await _db.Trips
                .Where(t => t.FeedVersionId == feedVersionId)
                .ToListAsync(ct))
            {
                tripLookup.TryAdd(t.TripId, t.Id);
            }

            // Phase 3: single-pass stop_times — derive route types + SqlBulkCopy
            _logStore.AddEntry(feedVersionId, "Phase 3: Bulk importing stop_times...");
            var routeTypesPerStop = new Dictionary<string, RouteType>(StringComparer.OrdinalIgnoreCase);

            using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(
                (Microsoft.Data.SqlClient.SqlConnection)conn,
                Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default,
                sqlTx)
            {
                DestinationTableName = "StopTimes",
                BatchSize = 50000,
                BulkCopyTimeout = 180
            };

            bulkCopy.ColumnMappings.Add("TripId", "TripId");
            bulkCopy.ColumnMappings.Add("RawStopId", "RawStopId");
            bulkCopy.ColumnMappings.Add("ArrivalTime", "ArrivalTime");
            bulkCopy.ColumnMappings.Add("DepartureTime", "DepartureTime");
            bulkCopy.ColumnMappings.Add("StopSequence", "StopSequence");
            bulkCopy.ColumnMappings.Add("StopHeadsign", "StopHeadsign");
            bulkCopy.ColumnMappings.Add("PickupType", "PickupType");
            bulkCopy.ColumnMappings.Add("DropOffType", "DropOffType");
            bulkCopy.ColumnMappings.Add("Timepoint", "Timepoint");

            var dt = new DataTable();
            dt.Columns.Add("TripId", typeof(int));
            dt.Columns.Add("RawStopId", typeof(string));
            dt.Columns.Add("ArrivalTime", typeof(int));
            dt.Columns.Add("DepartureTime", typeof(int));
            dt.Columns.Add("StopSequence", typeof(int));
            dt.Columns.Add("StopHeadsign", typeof(string));
            dt.Columns.Add("PickupType", typeof(int));
            dt.Columns.Add("DropOffType", typeof(int));
            dt.Columns.Add("Timepoint", typeof(bool));

            await foreach (var batch in _gtfs.ParseStopTimesBatchedAsync(archive, 50000))
            {
                dt.Rows.Clear();
                foreach (var st in batch)
                {
                    if (routeByTrip.TryGetValue(st.TripId, out var rId) &&
                        routeTypeByRoute.TryGetValue(rId, out var rt) &&
                        !routeTypesPerStop.ContainsKey(st.StopId))
                    {
                        routeTypesPerStop[st.StopId] = rt;
                    }

                    dt.Rows.Add(
                        tripLookup.GetValueOrDefault(st.TripId, 0),
                        st.StopId,
                        GtfsParserManager.ParseGtfsTimeToSeconds(st.ArrivalTime),
                        GtfsParserManager.ParseGtfsTimeToSeconds(st.DepartureTime),
                        st.StopSequence,
                        (object?)st.StopHeadsign ?? DBNull.Value,
                        (object?)st.PickupType ?? DBNull.Value,
                        (object?)st.DropOffType ?? DBNull.Value,
                        st.Timepoint.HasValue ? (object)(st.Timepoint == 1) : DBNull.Value
                    );
                }
                await bulkCopy.WriteToServerAsync(dt, ct);
            }

            _logStore.AddEntry(feedVersionId, "Phase 3: stop_times import complete");

            // Phase 4: save agencies and stops (with derived route types)
            _logStore.AddEntry(feedVersionId, $"Phase 4: Saving {agencies.Count} agencies and {rawStops.Count} stops...");
            foreach (var a in agencies)
                _db.Agencies.Add(new Agency
                {
                    FeedVersionId = feedVersionId,
                    AgencyId = a.AgencyId, Name = a.AgencyName, Url = a.AgencyUrl,
                    Timezone = a.AgencyTimezone, Language = a.AgencyLang, Phone = a.AgencyPhone,
                    FareUrl = a.AgencyFareUrl, Email = a.AgencyEmail, OperatorId = operatorId
                });

            foreach (var s in rawStops)
            {
                routeTypesPerStop.TryGetValue(s.StopId, out var rt);
                _db.RawStops.Add(new RawStop
                {
                    FeedVersionId = feedVersionId,
                    RawStopId = s.StopId,
                    Name = s.StopName, Lat = s.StopLat, Lon = s.StopLon,
                    StationType = MapGtfsLocationType(s.LocationType),
                    ParentRawStopId = s.ParentStation,
                    StopCode = s.StopCode, StopDesc = s.StopDesc, ZoneId = s.ZoneId,
                    PlatformCode = s.PlatformCode,
                    WheelchairBoarding = s.WheelchairBoarding.HasValue ? s.WheelchairBoarding == 1 : null,
                    RouteType = rt,
                    IsActive = true,
                    ReconciliationStatus = ReconciliationStatus.Pending
                });
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);
            _logStore.AddEntry(feedVersionId, "Phase 4: agencies and stops saved");

            // Reconcile raw stops into canonical stations
            _logStore.AddEntry(feedVersionId, "Reconciling stations...");
            await _reconciliation.ReconcileFeedVersionAsync(feedVersionId, ct);
            _logStore.AddEntry(feedVersionId, "Reconciliation complete");

            // Persist FK values (CanonicalStationId on RawStops) that reconciliation
            // set but couldn't save because AutoDetectChangesEnabled is false.
            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);

            // Ensure index exists for the backfill join
            _logStore.AddEntry(feedVersionId, "Ensuring backfill indexes...");
            await _db.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StopTimes_RawStopId') CREATE INDEX IX_StopTimes_RawStopId ON StopTimes (RawStopId) INCLUDE (CanonicalStationId, RawStopEntityId)", ct);

            // Backfill RawStopEntityId and CanonicalStationId on StopTimes
            _logStore.AddEntry(feedVersionId, "Backfilling station references...");
            try
            {
                var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE st SET st.RawStopEntityId = rs.Id, st.CanonicalStationId = rs.CanonicalStationId FROM StopTimes st INNER JOIN RawStops rs ON st.RawStopId = rs.RawStopId WHERE rs.FeedVersionId = {feedVersionId}", ct);
                _logStore.AddEntry(feedVersionId, $"Backfill complete: {rows} rows updated");
            }
            catch (Exception backfillEx)
            {
                version.ImportStatus = FeedImportStatus.Failed;
                version.ImportError = backfillEx.InnerException?.Message ?? backfillEx.Message;
                await _db.SaveChangesAsync(ct);
                _logStore.AddEntry(feedVersionId, $"Backfill failed: {version.ImportError}");
                throw;
            }

            // Match new canonical stations to places
            _logStore.AddEntry(feedVersionId, "Matching stations to places...");
            await _placeMatching.LoadPlacesAsync(ct);
            await _placeMatching.MatchStationsToPlacesAsync(ct);
            _logStore.AddEntry(feedVersionId, "Place matching complete");

            // Phase 5: finalize version
            _db.Entry(version).State = EntityState.Modified;

            var prevActive = await _db.FeedVersions
                .Where(fv => fv.FeedId == version.FeedId && fv.IsActive && fv.Id != feedVersionId)
                .ToListAsync(ct);
            foreach (var pv in prevActive)
                pv.IsActive = false;

            var prevActiveIds = prevActive.Select(pv => pv.Id).ToList();
            if (prevActiveIds.Count > 0)
                await _db.Shapes.Where(s => prevActiveIds.Contains(s.FeedVersionId)).ExecuteDeleteAsync(ct);

            var convexHull = _gtfs.ComputeConvexHull(rawStops);

            version.StopCount = rawStops.Count;
            version.RouteCount = routes.Count;
            version.TripCount = trips.Count;
            version.AgencyCount = agencies.Count;
            version.ConvexHull = convexHull;

            if (calendar.Count > 0)
            {
                if (DateOnly.TryParseExact(calendar.Min(c => c.StartDate), "yyyyMMdd", out var start))
                    version.ServiceLevelStart = start;
                if (DateOnly.TryParseExact(calendar.Max(c => c.EndDate), "yyyyMMdd", out var end))
                    version.ServiceLevelEnd = end;
            }
            else if (calendarDates.Count > 0)
            {
                if (DateOnly.TryParseExact(calendarDates.Min(cd => cd.Date), "yyyyMMdd", out var start))
                    version.ServiceLevelStart = start;
                if (DateOnly.TryParseExact(calendarDates.Max(cd => cd.Date), "yyyyMMdd", out var end))
                    version.ServiceLevelEnd = end;
            }

            version.IsActive = true;
            version.ImportStatus = FeedImportStatus.Success;
            version.ImportError = null;
            version.ImportedAt = DateTime.UtcNow;
            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);
            await sqlTx.CommitAsync(ct);

            _logStore.AddEntry(feedVersionId, $"Import complete: {routes.Count} routes, {rawStops.Count} stops, {trips.Count} trips");
            _logger.LogInformation("Import complete for FeedVersion {VersionId} ({RouteCount} routes, {StopCount} stops, {TripCount} trips)",
                feedVersionId, routes.Count, rawStops.Count, trips.Count);

            // Deactivate orphan CanonicalStations (Stop-type with no active RawStops from any active FeedVersion)
            var deactivated = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE cs SET IsActive = 0 FROM CanonicalStations cs INNER JOIN CanonicalStationOperators cso ON cso.CanonicalStationId = cs.Id WHERE cso.OperatorId = @p0 AND cs.IsActive = 1 AND cs.StationType = 'Stop' AND NOT EXISTS (SELECT 1 FROM RawStops rs INNER JOIN FeedVersions fv ON fv.Id = rs.FeedVersionId WHERE rs.CanonicalStationId = cs.Id AND rs.IsActive = 1 AND fv.IsActive = 1)",
                new object[] { operatorId }, ct);
            _logStore.AddEntry(feedVersionId, $"Deactivated {deactivated} orphan CanonicalStations with no stops");
            _logger.LogInformation("Deactivated {Count} orphan CanonicalStations for FeedVersion {VersionId}", deactivated, feedVersionId);

            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
        }
        catch (Exception ex)
        {
            try { using var rollbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token); await sqlTx.RollbackAsync(rollbackCts.Token); } catch { }
            _logStore.AddEntry(feedVersionId, $"Import failed: {ex.Message}");
            try
            {
                using var errConn = new Microsoft.Data.SqlClient.SqlConnection(sqlConn.ConnectionString);
                await errConn.OpenAsync(ct);
                await using var cmd = errConn.CreateCommand();
                cmd.CommandText = "UPDATE FeedVersions SET ImportStatus = @status, ImportError = @error WHERE Id = @id";
                cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@status", (int)FeedImportStatus.Failed));
                cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@error", ex.InnerException?.Message ?? ex.Message));
                cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@id", feedVersionId));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to write ImportStatus=Failed for FeedVersion {VersionId}", feedVersionId);
            }
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
            _logger.LogError(ex, "Import failed for FeedVersion {VersionId}", feedVersionId);
            throw;
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
            var sha1 = $"{feed.FeedId}-manual-{DateTime.UtcNow:yyyyMMddHHmmss}";
            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                sha1 = _gtfs.ComputeGtfsSha1(archive);
            }

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
            _logStore.AddEntry(version.Id, "Manual trigger, zip on disk, SHA1 computed");
        }
        else if (version.ImportStatus == FeedImportStatus.Success)
        {
            _logStore.AddEntry(version.Id, "Already up to date (previous import succeeded)");
            return version;
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

    public List<string> GetImportLogs(int versionId)
    {
        return _logStore.GetEntries(versionId);
    }

    public async Task<List<Feed>> GetActiveGtfsFeedsAsync(CancellationToken ct)
    {
        return await _db.Feeds
            .Where(f => f.IsActive && f.FeedType == FeedType.GTFSStatic)
            .ToListAsync(ct);
    }

    private static StationType MapGtfsLocationType(int? locationType) => locationType switch
    {
        null => StationType.Stop,
        0 => StationType.Stop,
        1 => StationType.Station,
        2 => StationType.Platform,
        3 => StationType.Platform,
        4 => StationType.Platform,
        _ => StationType.Stop
    };


}
