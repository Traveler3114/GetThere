using System.Collections.Concurrent;
using System.Data;
using System.IO.Compression;
using System.Text.Json;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using NetTopologySuite.Geometries;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Managers;

public class FeedManager
{
    private readonly TransitDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FeedManager> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly Services.GtfsParser _gtfs;
    private readonly OnestopIdManager _onestopId;
    private readonly ReconciliationManager _reconciliation;
    private readonly PlaceMatchingManager _placeMatching;
    private readonly Services.ImportLogStore _logStore;
    private readonly ExternalFeedSource _externalFeedSource;
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _feedLocks = new();

    public FeedManager(
        TransitDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<FeedManager> logger,
        IWebHostEnvironment env,
        Services.GtfsParser gtfs,
        OnestopIdManager onestopId,
        ReconciliationManager reconciliation,
        PlaceMatchingManager placeMatching,
        Services.ImportLogStore logStore,
        ExternalFeedSource externalFeedSource)
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
        _externalFeedSource = externalFeedSource;
    }

    public async Task<(List<FeedResponse> Feeds, int Total)> GetAllAsync(int page = 1, int perPage = 50, bool showInternal = false, CancellationToken ct = default)
    {
        var query = _db.Feeds
            .Include(f => f.Operator)
            .OrderBy(f => f.Id)
            .AsQueryable();

        if (!showInternal)
            query = query.Where(f => !f.IsInternal);

        var total = await query.CountAsync(ct);
        var feeds = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(FeedMapper.ToResponseExpression)
            .ToListAsync(ct);

        return (feeds, total);
    }

    public async Task<FeedResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.Feeds
            .Include(f => f.Operator)
            .Where(f => f.Id == id)
            .Select(FeedMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Feed> CreateAsync(
        int operatorId, string feedType,
        string feedId, string? url, int refreshIntervalSeconds, CancellationToken ct)
    {
        if (!Enum.TryParse<FeedType>(feedType, true, out var parsedFeedType))
            throw new Exceptions.AppException($"Invalid feed type '{feedType}'.", 400, "INVALID_FEED_TYPE");
        
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new Exceptions.AppException("Invalid feed URL. Must be an absolute HTTP(S) URL.", 400, "INVALID_FEED_URL");
        if (parsedFeedType == FeedType.GTFSStatic && url is not null && !url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            _logger.LogWarning("Feed {FeedId} static URL does not end with .zip — may not be a valid GTFS archive", feedId);
        if (refreshIntervalSeconds < 60)
            throw new Exceptions.AppException("Refresh interval must be at least 60 seconds.", 400, "INVALID_REFRESH_INTERVAL");

        var op = await _db.Operators.FindAsync([operatorId], ct)
            ?? throw new Exceptions.AppException("Operator not found.", 404);

        var typeSuffix = parsedFeedType switch
        {
            FeedType.GTFSStatic => "gtfs-static",
            FeedType.GTFSRealtime => "gtfs-rt",
            FeedType.GBFS => "gbfs",
            _ => parsedFeedType.ToString().ToLowerInvariant()
        };
        var onestopId = _onestopId.GenerateFeedOnestopId(0, 0, $"{feedId}-{typeSuffix}");
        if (await _db.Feeds.AnyAsync(f => f.OnestopId == onestopId, ct))
            throw new InvalidOperationException($"Feed with OnestopId '{onestopId}' already exists.");
        if (await _db.Feeds.AnyAsync(f => f.FeedId == feedId, ct))
            throw new InvalidOperationException($"Feed with FeedId '{feedId}' already exists.");

        var feed = new Feed
        {
            OnestopId = onestopId,
            OperatorId = operatorId,
            FeedType = parsedFeedType,
            FeedId = feedId,
            Url = url,
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

        if (request.Url is not null && (!Uri.TryCreate(request.Url, UriKind.Absolute, out var extUri) || extUri.Scheme is not ("http" or "https")))
            return (false, "Invalid feed URL. Must be an absolute HTTP(S) URL.");
        if (request.LicenseUrl is not null && !Uri.TryCreate(request.LicenseUrl, UriKind.Absolute, out _))
            return (false, "Invalid license URL.");

        if (request.RefreshIntervalSeconds < 60)
            return (false, "Refresh interval must be at least 60 seconds.");

        feed.Url = request.Url;
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

        // Remove CanonicalStationOperator links that no longer have any active RawStop support
        var operatorId = feed.OperatorId;
        await _db.CanonicalStationOperators
            .Where(cso => cso.OperatorId == operatorId
                && !_db.RawStops.Any(rs => rs.CanonicalStationId == cso.CanonicalStationId && rs.IsActive))
            .ExecuteDeleteAsync(ct);

        // Deactivate CanonicalStations that now have no operator links and no active raw stops
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE CanonicalStations SET IsActive = 0 WHERE IsActive = 1 AND StationType = 'Stop' "
            + "AND NOT EXISTS (SELECT 1 FROM CanonicalStationOperators WHERE CanonicalStationId = Id) "
            + "AND NOT EXISTS (SELECT 1 FROM RawStops WHERE CanonicalStationId = Id AND IsActive = 1)");

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

        _feedLocks.TryRemove(id, out _);

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

            string? remoteLastModified = null;
            string? remoteETag = null;

            // Removed: HEAD/ETag pre-check. It matched against any past FeedVersion (including
            // inactive/failed ones) and short-circuited real re-imports whenever an upstream
            // server returned a stale or unstable ETag/Last-Modified. The SHA1 check below,
            // done on full downloaded content, is the reliable "unchanged" signal — no need
            // for a second, less trustworthy one.

            var source = _externalFeedSource;
            var result = await source.FetchDataAsync(feed, ct);

            if (result.AlreadyHandled)
            {
                _logger.LogInformation("Feed {FeedId}: import handled directly by source, skipping disk write", feed.FeedId);
                return null;
            }

            // Content-type validation for external HTTP responses
            if (result.ContentType is not null
                && result.ContentType != "application/zip"
                && !result.ContentType.StartsWith("application/"))
            {
                _logger.LogWarning("Feed {FeedId} has unexpected Content-Type {ContentType}, skipping", feed.FeedId, result.ContentType);
                return null;
            }

            var outputDir = Path.Combine(_env.ContentRootPath, "feeds", feed.FeedId);
            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to create directory {Dir} for feed {FeedId}", outputDir, feed.FeedId); return null; }
            var zipPath = Path.Combine(outputDir, "gtfs.zip");
            var tmpPath = zipPath + ".tmp";
            await File.WriteAllBytesAsync(tmpPath, result.Data, ct);
            try
            {
                File.Replace(tmpPath, zipPath, null);
            }
            catch (FileNotFoundException)
            {
                File.Move(tmpPath, zipPath);
            }

            var sha1 = source.ComputeHash(feed, result.Data);

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
                IsActive = false,
                LastModified = remoteLastModified is not null && DateTimeOffset.TryParse(remoteLastModified, out var lm) ? lm.UtcDateTime : null,
                ETag = remoteETag
            };

            _db.FeedVersions.Add(version);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("New FeedVersion {VersionId} for feed {FeedId} SHA1={Sha1}", version.Id, feed.FeedId, sha1);
            _logStore.AddEntry(version.Id, $"Downloaded {result.Data.Length:N0} bytes via {source.GetType().Name}");
            _logStore.AddEntry(version.Id, $"SHA1 = {sha1}");
            return version;
        }
        finally
        {
            sem.Release();
        }
    }

    // Single bad record fails the entire import. Acceptable for current feed quality (ZET, HZPP).
    // Revisit for noisier Phase 2 feeds.
    public async Task ImportFeedVersionAsync(int feedVersionId, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Include(fv => fv.Feed)
                .ThenInclude(f => f.Operator)
            .FirstOrDefaultAsync(fv => fv.Id == feedVersionId, ct);

        if (version is null) throw new InvalidOperationException("FeedVersion not found.");

        var feedLock = _feedLocks.GetOrAdd(version.Feed.Id, _ => new SemaphoreSlim(1, 1));
        await feedLock.WaitAsync(CancellationToken.None);
        try
        {
            // Re-fetch version after acquiring lock — another caller may have
            // already imported it while we were waiting.
            version = await _db.FeedVersions
                .Include(fv => fv.Feed)
                    .ThenInclude(f => f.Operator)
                .FirstOrDefaultAsync(fv => fv.Id == feedVersionId, ct);
            if (version is null) throw new InvalidOperationException("FeedVersion not found.");
            if (version.ImportStatus == FeedImportStatus.Success)
            {
                _logger.LogInformation("FeedVersion {VersionId} already imported, skipping", feedVersionId);
                return;
            }

            var tempZipPath = PrepareImportTempZip(version, feedVersionId);
            if (tempZipPath is null)
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            await _db.SaveChangesAsync(ct);

            using var archive = ZipFile.OpenRead(tempZipPath);
            _logStore.AddEntry(feedVersionId, "Opening GTFS archive...");

            var (sqlConn, sqlTx) = await BeginImportTransactionAsync(ct);
            try
            {
                _db.Database.SetCommandTimeout(600);
                _db.ChangeTracker.AutoDetectChangesEnabled = false;
                try
                {
                    await ValidateAndCleanupAsync(feedVersionId, version, archive, tempZipPath, sqlTx, ct);
                    if (version.ImportStatus == FeedImportStatus.Failed) return;

                    _db.ChangeTracker.Clear();

                    var parsed = await ParseGtfsFilesAsync(feedVersionId, version, archive, tempZipPath, sqlTx, ct);
                    if (parsed is null) return;

                    await AutoGenerateShapesIfMissing(feedVersionId, parsed, archive, ct);

                    var carryForwardShapeIds = await CarryForwardManualEditsAsync(feedVersionId, version, parsed, archive, ct);

                    var canonicalRouteLookup = await ImportRoutesPhaseAsync(feedVersionId, version, parsed.Routes, parsed.RawStops, parsed.OperatorId, ct);

                    var prefix = $"gt-{version.Feed.FeedId}-";
                    var tripLookup = await ImportTripsShapesCalendarsPhaseAsync(feedVersionId, version, prefix, parsed.Trips, parsed.Shapes, parsed.Calendar, parsed.CalendarDates, canonicalRouteLookup, carryForwardShapeIds, ct);

                    await BackfillRouteGeometriesAsync(feedVersionId, canonicalRouteLookup, ct);

                    var routeTypesPerStop = await ImportStopTimesBulkPhaseAsync(feedVersionId, archive, tripLookup, parsed.RouteByTrip, parsed.RouteTypeByRoute, sqlConn, sqlTx, ct);

                    await ImportAgenciesAndStopsPhaseAsync(feedVersionId, parsed.Agencies, parsed.RawStops, routeTypesPerStop, parsed.OperatorId, ct);

                    archive.Dispose();

                    await FinalizeVersionPhaseAsync(feedVersionId, version, parsed.OperatorId, parsed.Routes, parsed.RawStops, parsed.Trips, parsed.Calendar, parsed.CalendarDates, parsed.Agencies, tempZipPath, sqlTx, ct);
                }
                finally
                {
                    _db.ChangeTracker.AutoDetectChangesEnabled = true;
                    _db.Database.SetCommandTimeout(30);
                }
            }
            catch (Exception ex)
            {
                await HandleImportErrorAsync(feedVersionId, sqlConn, sqlTx, ex, tempZipPath, ct);
                throw;
            }

            // Clear the committed transaction from the context so subsequent
            // SaveChangesAsync calls (reconciliation, place matching) don't try to
            // create savepoints on an already-committed transaction.
            _db.Database.UseTransaction(null);

            try
            {
                await ReconcileAndBackfillAsync(feedVersionId, version, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed after successful import for FeedVersion {VersionId}", feedVersionId);
                _logStore.AddEntry(feedVersionId, $"Reconciliation failed (non-fatal): {ex.Message}");
            }
            try
            {
                await MatchPlacesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Place matching failed after successful import for FeedVersion {VersionId}", feedVersionId);
                _logStore.AddEntry(feedVersionId, $"Place matching failed (non-fatal): {ex.Message}");
            }
        }
        finally
        {
            feedLock.Release();
        }
    }

    public async Task<FeedVersion> TriggerImportAsync(int feedId, CancellationToken ct)
    {
        var version = await CheckAndFetchAsync(feedId, ct);
        if (version is null)
        {
            var feed = await _db.Feeds.FindAsync([feedId], ct);
            if (feed is null) throw new InvalidOperationException("Feed not found.");

            if (feed.IsInternal)
            {
                // Internal feeds fetch + import themselves. CheckAndFetchAsync returning null here just
                // means that already ran (no-op if unchanged, or already imported+activated).
                // There is no zip on disk for these — do NOT fall into the zip pipeline below.
                var active = await GetActiveFeedVersionAsync(feedId, ct);
                if (active is not null) return active;

                throw new Exceptions.AppException(
                    "Internal feed ran but produced no active version — check its run history for errors.",
                    500, "InternalFeedNoActiveVersion");
            }

            var zipPath = Path.Combine(_env.ContentRootPath, "feeds", feed.FeedId, "gtfs.zip");
            var sha1 = $"{feed.FeedId}-manual-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():n}";
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
        if (version.ImportStatus != FeedImportStatus.Success)
            await ImportFeedVersionAsync(version.Id, ct);
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
            .Where(f => f.IsActive
                && f.FeedType == FeedType.GTFSStatic
                && f.Url != null)
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

    private string? PrepareImportTempZip(FeedVersion version, int feedVersionId)
    {
        var zipPath = Path.Combine(_env.ContentRootPath, "feeds", version.Feed.FeedId, "gtfs.zip");
        if (!File.Exists(zipPath))
        {
            version.ImportStatus = FeedImportStatus.Failed;
            version.ImportError = "GTFS zip not found on disk";
            _logStore.AddEntry(feedVersionId, "Error: GTFS zip not found on disk");
            return null;
        }

        version.ImportStatus = FeedImportStatus.Importing;
        var importTempDir = Path.Combine(Path.GetTempPath(), "gtfs-imports");
        try { Directory.CreateDirectory(importTempDir); } catch { throw new TransitInfoAPI.Exceptions.AppException("Failed to create temporary directory for import.", 500, "ImportTempDirFailed"); }
        var tempZipPath = Path.Combine(importTempDir, $"import-{feedVersionId}-{Guid.NewGuid()}.zip");
        File.Copy(zipPath, tempZipPath, overwrite: true);
        _logStore.AddEntry(feedVersionId, "Copied GTFS zip to temporary working file");
        _logStore.AddEntry(feedVersionId, "Import started");
        return tempZipPath;
    }

    private async Task<(Microsoft.Data.SqlClient.SqlConnection SqlConn, Microsoft.Data.SqlClient.SqlTransaction SqlTx)> BeginImportTransactionAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
        var sqlConn = (Microsoft.Data.SqlClient.SqlConnection)conn;
        var existingTx = _db.Database.CurrentTransaction;
        Microsoft.Data.SqlClient.SqlTransaction sqlTx;
        if (existingTx is not null)
        {
            sqlTx = (Microsoft.Data.SqlClient.SqlTransaction)existingTx.GetDbTransaction();
        }
        else
        {
            sqlTx = sqlConn.BeginTransaction();
            _db.Database.UseTransaction(sqlTx);
        }
        return (sqlConn, sqlTx);
    }

    private async Task ValidateAndCleanupAsync(int feedVersionId, FeedVersion version, ZipArchive archive, string tempZipPath, Microsoft.Data.SqlClient.SqlTransaction sqlTx, CancellationToken ct)
    {
        _logStore.AddEntry(feedVersionId, "Validating GTFS files...");
        var validation = _gtfs.ValidateGtfs(archive);
        if (!validation.IsValid)
        {
            version.ImportStatus = FeedImportStatus.Failed;
            version.ImportError = "GTFS validation failed: " + string.Join("; ", validation.Errors);
            _logStore.AddEntry(feedVersionId, $"Validation failed: {string.Join("; ", validation.Errors)}");
            await _db.SaveChangesAsync(ct);
            await sqlTx.CommitAsync(ct);
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp file {Path}", tempZipPath); }
            return;
        }

        _logStore.AddEntry(feedVersionId, "Cleaning up existing data for this feed version...");
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM StopTimes WHERE TripId IN (SELECT Id FROM Trips WHERE FeedVersionId = @p0)",
            new object[] { feedVersionId }, ct);
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE StopTimes SET RawStopEntityId = NULL WHERE RawStopEntityId IN (SELECT Id FROM RawStops WHERE FeedVersionId = @p0)",
            new object[] { feedVersionId }, ct);
        await _db.CalendarDates.Where(cd => cd.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
        await _db.Calendars.Where(c => c.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
        await _db.Shapes.Where(s => s.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
        await _db.Trips.Where(t => t.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
        await _db.ReconciliationCandidates.Where(rc => rc.RawStop.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
        await _db.RawStops.Where(rs => rs.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
        await _db.Agencies.Where(a => a.FeedVersionId == feedVersionId).ExecuteDeleteAsync(ct);
        _logStore.AddEntry(feedVersionId, "Cleanup complete");
        _logStore.AddEntry(feedVersionId, "Validation passed");
    }

    private async Task AutoGenerateShapesIfMissing(int feedVersionId, ParsedGtfsData parsed, ZipArchive archive, CancellationToken ct)
    {
        if (parsed.Shapes.Count > 0) return;

        _logStore.AddEntry(feedVersionId, "No shapes.txt found — generating from stop_times...");

        var stopTimes = _gtfs.ParseStopTimes(archive);

        var byTrip = new Dictionary<string, List<RawStopTimeRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in stopTimes)
        {
            if (!byTrip.TryGetValue(st.TripId, out var list))
                byTrip[st.TripId] = list = [];
            list.Add(st);
        }

        foreach (var list in byTrip.Values)
            list.Sort((a, b) => a.StopSequence.CompareTo(b.StopSequence));

        var stopsByStopId = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in parsed.RawStops)
            stopsByStopId[s.StopId] = (s.StopLat, s.StopLon);

        var tripMap = new Dictionary<string, RawTripRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in parsed.Trips)
            tripMap[t.TripId] = t;

        var shapesFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var generatedCount = 0;

        foreach (var trip in parsed.Trips)
        {
            if (!byTrip.TryGetValue(trip.TripId, out var tripStopTimes))
                continue;

            var coords = new List<Coordinate>(tripStopTimes.Count);
            foreach (var st in tripStopTimes)
            {
                if (!stopsByStopId.TryGetValue(st.StopId, out var stop))
                    continue;
                if (stop.Lat == 0.0 && stop.Lon == 0.0)
                    continue;
                coords.Add(new Coordinate(stop.Lon, stop.Lat));
            }

            if (coords.Count < 2)
                continue;

            var shapeId = $"gen-{trip.TripId}";
            if (!parsed.Shapes.ContainsKey(shapeId))
                parsed.Shapes[shapeId] = shapesFactory.CreateLineString(coords.ToArray());
            trip.ShapeId = shapeId;
            generatedCount++;
        }

        _logStore.AddEntry(feedVersionId,
            $"Generated {generatedCount} shapes from {parsed.Shapes.Count} unique paths for {parsed.Trips.Count} trips");
    }

    private async Task<HashSet<string>> CarryForwardManualEditsAsync(int feedVersionId, FeedVersion version, ParsedGtfsData parsed, ZipArchive archive, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var prevActive = await _db.FeedVersions
            .FirstOrDefaultAsync(fv => fv.FeedId == version.Feed.Id && fv.IsActive && fv.Id != feedVersionId, ct);
        if (prevActive is null) return result;

        var oldEditedShapes = await _db.Shapes
            .Where(s => s.FeedVersionId == prevActive.Id && s.IsManuallyEdited)
            .ToListAsync(ct);
        if (oldEditedShapes.Count == 0) return result;

        var oldEditedShapeIds = oldEditedShapes.Select(s => s.ShapeId).ToList();

        var oldStopData = await _db.StopTimes
            .Where(st => st.Trip.FeedVersionId == prevActive.Id
                && st.Trip.ShapeId != null
                && oldEditedShapeIds.Contains(st.Trip.ShapeId))
            .OrderBy(st => st.TripId).ThenBy(st => st.StopSequence)
            .Select(st => new { ShapeId = st.Trip.ShapeId ?? "", st.Trip.TripId, st.RawStopId })
            .ToListAsync(ct);

        var oldSequences = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sd in oldStopData)
        {
            if (!oldSequences.TryGetValue(sd.ShapeId, out var byTrip))
                oldSequences[sd.ShapeId] = byTrip = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!byTrip.TryGetValue(sd.TripId, out var list))
                byTrip[sd.TripId] = list = [];
            list.Add(sd.RawStopId);
        }

        var newStopTimes = _gtfs.ParseStopTimes(archive);
        var newSequences = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in newStopTimes)
        {
            if (!newSequences.TryGetValue(st.TripId, out var list))
                newSequences[st.TripId] = list = [];
            list.Add(st.StopId);
        }

        var shapeIdSet = new HashSet<string>(parsed.Shapes.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var oldShape in oldEditedShapes)
        {
            if (!shapeIdSet.Contains(oldShape.ShapeId))
                continue;

            if (!oldSequences.TryGetValue(oldShape.ShapeId, out var oldByTrip))
                continue;

            var newTrips = parsed.Trips.Where(t =>
                string.Equals(t.ShapeId, oldShape.ShapeId, StringComparison.OrdinalIgnoreCase)).ToList();

            if (newTrips.Count == 0)
                continue;

            var allMatch = true;

            foreach (var newTrip in newTrips)
            {
                if (!oldByTrip.TryGetValue(newTrip.TripId, out var oldTripStops))
                    continue;

                if (!newSequences.TryGetValue(newTrip.TripId, out var newTripStops))
                {
                    allMatch = false;
                    break;
                }

                if (oldTripStops.Count != newTripStops.Count)
                {
                    allMatch = false;
                    break;
                }

                for (int i = 0; i < oldTripStops.Count; i++)
                {
                    if (!string.Equals(oldTripStops[i], newTripStops[i], StringComparison.OrdinalIgnoreCase))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (!allMatch) break;
            }

            if (allMatch && oldShape.Geometry is not null)
            {
                result.Add(oldShape.ShapeId);
                parsed.Shapes[oldShape.ShapeId] = oldShape.Geometry;
            }
        }

        if (result.Count > 0)
            _logStore.AddEntry(feedVersionId, $"Carried forward {result.Count} manually-edited shape(s) from previous feed version");

        return result;
    }

    private record ParsedGtfsData(
        List<RawAgencyRecord> Agencies,
        List<RawStopRecord> RawStops,
        List<RawRouteRecord> Routes,
        List<RawTripRecord> Trips,
        List<RawCalendarRecord> Calendar,
        List<RawCalendarDateRecord> CalendarDates,
        Dictionary<string, NetTopologySuite.Geometries.LineString> Shapes,
        int OperatorId,
        Dictionary<string, RouteType> RouteTypeByRoute,
        Dictionary<string, string> RouteByTrip);

    private async Task<ParsedGtfsData?> ParseGtfsFilesAsync(int feedVersionId, FeedVersion version, ZipArchive archive, string tempZipPath, Microsoft.Data.SqlClient.SqlTransaction sqlTx, CancellationToken ct)
    {
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

        if (calendar.Count == 0 && calendarDates.Count == 0)
        {
            version.ImportStatus = FeedImportStatus.Failed;
            version.ImportError = "Feed has no calendar or calendar_dates data — zero valid services. No departures will appear for any stop.";
            _logStore.AddEntry(feedVersionId, $"Import failed: {version.ImportError}");
            await _db.SaveChangesAsync(ct);
            await sqlTx.CommitAsync(ct);
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
            return null;
        }

        var operatorId = version.Feed.OperatorId;
        var routeTypeByRoute = routes
            .GroupBy(r => r.RouteId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RouteTypeEnum, StringComparer.OrdinalIgnoreCase);
        var routeByTrip = trips
            .GroupBy(t => t.TripId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RouteId, StringComparer.OrdinalIgnoreCase);

        return new ParsedGtfsData(agencies, rawStops, routes, trips, calendar, calendarDates, shapes, operatorId, routeTypeByRoute, routeByTrip);
    }

    private async Task<Dictionary<string, int>> ImportRoutesPhaseAsync(int feedVersionId, FeedVersion version, List<RawRouteRecord> routes, List<RawStopRecord> rawStops, int operatorId, CancellationToken ct)
    {
        _logStore.AddEntry(feedVersionId, "Phase 1: Saving canonical routes...");

        var existingRoutes = await _db.CanonicalRoutes
            .Where(cr => cr.OperatorId == operatorId)
            .ToListAsync(ct);
        var existingByOnestopId = new Dictionary<string, CanonicalRoute>(StringComparer.OrdinalIgnoreCase);
        foreach (var cr in existingRoutes)
            existingByOnestopId[cr.OnestopId] = cr;

        var seenOnestopIds = new HashSet<string>(existingRoutes.Select(cr => cr.OnestopId), StringComparer.OrdinalIgnoreCase);
        var onestopToRouteIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in routes)
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

            if (!onestopToRouteIds.TryGetValue(routeOnestopId, out var routeIds))
                onestopToRouteIds[routeOnestopId] = routeIds = [];
            routeIds.Add(r.RouteId);

            if (existingByOnestopId.ContainsKey(routeOnestopId))
                continue;

            var uniqueOnestopId = routeOnestopId;
            var dedupSuffix = 2;
            while (!seenOnestopIds.Add(uniqueOnestopId))
                uniqueOnestopId = $"{routeOnestopId}-{dedupSuffix++}";

            _db.CanonicalRoutes.Add(new CanonicalRoute
            {
                OnestopId = uniqueOnestopId,
                ShortName = r.RouteShortName,
                LongName = r.RouteLongName,
                RouteType = r.RouteTypeEnum,
                Color = r.RouteColor,
                TextColor = r.RouteTextColor,
                IsActive = true,
                OperatorId = operatorId
            });
        }

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);
        _logStore.AddEntry(feedVersionId, $"Phase 1: {routes.Count} routes saved");

        var prefix = $"gt-{version.Feed.FeedId}-";
        var allRoutes = await _db.CanonicalRoutes
            .Where(cr => cr.OperatorId == operatorId)
            .ToListAsync(ct);

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cr in allRoutes)
        {
            if (onestopToRouteIds.TryGetValue(cr.OnestopId, out var routeIds))
            {
                foreach (var routeId in routeIds)
                    result[prefix + routeId.ToLowerInvariant()] = cr.Id;
            }
        }
        return result;
    }

    private async Task<Dictionary<string, int>> ImportTripsShapesCalendarsPhaseAsync(int feedVersionId, FeedVersion version, string prefix, List<RawTripRecord> trips, Dictionary<string, NetTopologySuite.Geometries.LineString> shapes, List<RawCalendarRecord> calendar, List<RawCalendarDateRecord> calendarDates, Dictionary<string, int> canonicalRouteLookup, HashSet<string> carryForwardShapeIds, CancellationToken ct)
    {
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
                WheelchairAccessible = t.WheelchairAccessible switch { 1 => true, 2 => false, _ => null },
                BikesAllowed = t.BikesAllowed.HasValue ? t.BikesAllowed == 1 : null,
                CanonicalRouteId = canonicalRouteLookup.GetValueOrDefault(globalId)
            });
        }

        foreach (var kvp in shapes)
            _db.Shapes.Add(new Shape { FeedVersionId = feedVersionId, ShapeId = kvp.Key, Geometry = kvp.Value, IsManuallyEdited = carryForwardShapeIds.Contains(kvp.Key) });

        foreach (var c in calendar)
            _db.Calendars.Add(new Calendar
            {
                FeedVersionId = feedVersionId, ServiceId = c.ServiceId,
                Monday = c.Monday == 1, Tuesday = c.Tuesday == 1, Wednesday = c.Wednesday == 1,
                Thursday = c.Thursday == 1, Friday = c.Friday == 1, Saturday = c.Saturday == 1,
                Sunday = c.Sunday == 1,
                StartDate = DateOnly.ParseExact(c.StartDate, "yyyyMMdd"),
                EndDate = DateOnly.ParseExact(c.EndDate, "yyyyMMdd")
            });

        foreach (var cd in calendarDates)
        {
            if (cd.ExceptionType < 1 || cd.ExceptionType > 2)
            {
                _logger.LogWarning("Skipping calendar_date with invalid exception_type {Type} for service {ServiceId}", cd.ExceptionType, cd.ServiceId);
                continue;
            }
            _db.CalendarDates.Add(new CalendarDate
            {
                FeedVersionId = feedVersionId, ServiceId = cd.ServiceId,
                Date = DateOnly.ParseExact(cd.Date, "yyyyMMdd"),
                ExceptionType = cd.ExceptionType
            });
        }

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);
        _logStore.AddEntry(feedVersionId, "Phase 2: trips, shapes, calendars saved");

        var tripLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in await _db.Trips.Where(t => t.FeedVersionId == feedVersionId).ToListAsync(ct))
            tripLookup.TryAdd(t.TripId, t.Id);
        return tripLookup;
    }

    private async Task BackfillRouteGeometriesAsync(int feedVersionId, Dictionary<string, int> canonicalRouteLookup, CancellationToken ct)
    {
        var routeIds = canonicalRouteLookup.Values.ToList();
        var shapeCounts = await _db.Trips
            .Where(t => t.FeedVersionId == feedVersionId && t.CanonicalRouteId.HasValue && t.ShapeId != null)
            .GroupBy(t => new { t.CanonicalRouteId, t.ShapeId })
            .Select(g => new { g.Key.CanonicalRouteId, g.Key.ShapeId, Count = g.Count() })
            .ToListAsync(ct);
        var routeShapes = shapeCounts
            .GroupBy(g => g.CanonicalRouteId)
            .Select(g => new
            {
                RouteId = g.Key!.Value,
                MostCommonShapeId = g.OrderByDescending(x => x.Count).Select(x => x.ShapeId!).FirstOrDefault()
            })
            .ToList();

        var shapeData = await _db.Shapes
            .Where(s => s.FeedVersionId == feedVersionId)
            .ToDictionaryAsync(s => s.ShapeId, s => new { s.Geometry, s.IsManuallyEdited }, ct);

        var canonicalRoutes = await _db.CanonicalRoutes
            .Where(cr => routeIds.Contains(cr.Id)).ToListAsync(ct);
        var routeLookup = canonicalRoutes.ToDictionary(r => r.Id);

        foreach (var rs in routeShapes)
        {
            if (routeLookup.TryGetValue(rs.RouteId, out var cr) &&
                shapeData.TryGetValue(rs.MostCommonShapeId!, out var sd))
            {
                cr.Geometry = sd.Geometry;
                cr.ShapeEdited = sd.IsManuallyEdited;
            }
        }

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);
        _logStore.AddEntry(feedVersionId, "Phase 2: route geometries backfilled from trip shapes");
    }

    private async Task<Dictionary<string, RouteType>> ImportStopTimesBulkPhaseAsync(int feedVersionId, ZipArchive archive, Dictionary<string, int> tripLookup, Dictionary<string, string> routeByTrip, Dictionary<string, RouteType> routeTypeByRoute, Microsoft.Data.SqlClient.SqlConnection sqlConn, Microsoft.Data.SqlClient.SqlTransaction sqlTx, CancellationToken ct)
    {
        _logStore.AddEntry(feedVersionId, "Phase 3: Bulk importing stop_times...");
        var routeTypesPerStop = new Dictionary<string, RouteType>(StringComparer.OrdinalIgnoreCase);

        using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(sqlConn, Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default, sqlTx)
        {
            DestinationTableName = "StopTimes", BatchSize = 50000, BulkCopyTimeout = 180
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

        await foreach (var batch in _gtfs.ParseStopTimesBatchedAsync(archive, 50000))
        {
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
            foreach (var st in batch)
            {
                if (routeByTrip.TryGetValue(st.TripId, out var rId) &&
                    routeTypeByRoute.TryGetValue(rId, out var rt) &&
                    !routeTypesPerStop.ContainsKey(st.StopId))
                    routeTypesPerStop[st.StopId] = rt;

                if (!tripLookup.TryGetValue(st.TripId, out var resolvedTripId))
                {
                    _logger.LogWarning("Skipping stop_times row referencing unknown trip_id {TripId}", st.TripId);
                    continue;
                }

                var arrival = Services.GtfsParser.ParseGtfsTimeToSeconds(st.ArrivalTime);
                var departure = Services.GtfsParser.ParseGtfsTimeToSeconds(st.DepartureTime);
                if (departure is null)
                {
                    _logger.LogWarning("Skipping stop_times row with invalid departure time for trip {TripId} stop {StopId}", st.TripId, st.StopId);
                    continue;
                }

                var pickupType = st.PickupType is >= 0 and <= 3 ? (object?)st.PickupType : DBNull.Value;
                if (st.PickupType is not (null or >= 0 and <= 3))
                    _logger.LogWarning("Invalid PickupType {Value} for stop {StopId}", st.PickupType, st.StopId);
                var dropOffType = st.DropOffType is >= 0 and <= 3 ? (object?)st.DropOffType : DBNull.Value;
                if (st.DropOffType is not (null or >= 0 and <= 3))
                    _logger.LogWarning("Invalid DropOffType {Value} for stop {StopId}", st.DropOffType, st.StopId);
                dt.Rows.Add(
                    resolvedTripId, st.StopId,
                    (object?)arrival ?? DBNull.Value,
                    departure.Value,
                    st.StopSequence, (object?)st.StopHeadsign ?? DBNull.Value,
                    pickupType, dropOffType,
                    st.Timepoint.HasValue ? (object)(st.Timepoint == 1) : DBNull.Value);
            }
            await bulkCopy.WriteToServerAsync(dt, ct);
        }

        _logStore.AddEntry(feedVersionId, "Phase 3: stop_times import complete");
        return routeTypesPerStop;
    }

    private async Task ImportAgenciesAndStopsPhaseAsync(int feedVersionId, List<RawAgencyRecord> agencies, List<RawStopRecord> rawStops, Dictionary<string, RouteType> routeTypesPerStop, int operatorId, CancellationToken ct)
    {
        _logStore.AddEntry(feedVersionId, $"Phase 4: Saving {agencies.Count} agencies and {rawStops.Count} stops...");
        var seenAgencyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in agencies)
        {
            var key = string.IsNullOrWhiteSpace(a.AgencyId) ? "__default__" : a.AgencyId;
            if (!seenAgencyIds.Add(key))
            {
                _logger.LogWarning("Skipping duplicate or empty agency_id in FeedVersion {FeedVersionId}", feedVersionId);
                continue;
            }
            _db.Agencies.Add(new Agency
            {
                FeedVersionId = feedVersionId, AgencyId = a.AgencyId, Name = a.AgencyName,
                Url = a.AgencyUrl, Timezone = a.AgencyTimezone, Language = a.AgencyLang,
                Phone = a.AgencyPhone, FareUrl = a.AgencyFareUrl, Email = a.AgencyEmail,
                OperatorId = operatorId
            });
        }

        var seenStopIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in rawStops)
        {
            if (!seenStopIds.Add(s.StopId))
            {
                _logger.LogWarning("Skipping duplicate stop_id {StopId} in FeedVersion {FeedVersionId}", s.StopId, feedVersionId);
                continue;
            }
            _db.RawStops.Add(new RawStop
            {
                FeedVersionId = feedVersionId, RawStopId = s.StopId, Name = s.StopName,
                Lat = s.StopLat, Lon = s.StopLon, StationType = MapGtfsLocationType(s.LocationType),
                ParentRawStopId = s.ParentStation, StopCode = s.StopCode, StopDesc = s.StopDesc,
                ZoneId = s.ZoneId, PlatformCode = s.PlatformCode,
                WheelchairBoarding = s.WheelchairBoarding.HasValue ? s.WheelchairBoarding == 1 : null,
                RouteType = routeTypesPerStop.TryGetValue(s.StopId, out var rt) ? rt : null,
                IsActive = true, ReconciliationStatus = ReconciliationStatus.Pending
            });
        }

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);
        _logStore.AddEntry(feedVersionId, "Phase 4: agencies and stops saved");
    }

    private async Task ReconcileAndBackfillAsync(int feedVersionId, FeedVersion version, CancellationToken ct)
    {
        _logStore.AddEntry(feedVersionId, "Reconciling stations...");
        await _reconciliation.ReconcileFeedVersionAsync(feedVersionId, ct);
        _logStore.AddEntry(feedVersionId, "Reconciliation complete");

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);

        _logStore.AddEntry(feedVersionId, "Backfilling station references...");
        try
        {
            // This UPDATE joins StopTimes (potentially millions of rows) with RawStops
            // and may exceed the default 30s timeout. Raise it for this query only.
            var prevTimeout = _db.Database.GetCommandTimeout();
            _db.Database.SetCommandTimeout(600);
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE st SET st.RawStopEntityId = rs.Id, st.CanonicalStationId = rs.CanonicalStationId FROM StopTimes st INNER JOIN RawStops rs ON st.RawStopId = rs.RawStopId WHERE rs.FeedVersionId = {feedVersionId}", ct);
            _db.Database.SetCommandTimeout(prevTimeout);
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
    }

    private async Task MatchPlacesAsync(CancellationToken ct)
    {
        _logStore.AddEntry(0, "Matching stations to places...");
        await _placeMatching.LoadPlacesAsync(ct);
        await _placeMatching.MatchStationsToPlacesAsync(ct);
        _logStore.AddEntry(0, "Place matching complete");
    }

    private async Task FinalizeVersionPhaseAsync(int feedVersionId, FeedVersion version, int operatorId, List<RawRouteRecord> routes, List<RawStopRecord> rawStops, List<RawTripRecord> trips, List<RawCalendarRecord> calendar, List<RawCalendarDateRecord> calendarDates, List<RawAgencyRecord> agencies, string tempZipPath, Microsoft.Data.SqlClient.SqlTransaction sqlTx, CancellationToken ct)
    {
        version = (await _db.FeedVersions.Include(fv => fv.Feed).FirstOrDefaultAsync(fv => fv.Id == feedVersionId, ct))!;

        var prevActive = await _db.FeedVersions
            .Where(fv => fv.FeedId == version.Feed.Id && fv.IsActive && fv.Id != feedVersionId)
            .ToListAsync(ct);
        foreach (var pv in prevActive)
            pv.IsActive = false;

        var prevActiveIds = prevActive.Select(pv => pv.Id).ToList();
        if (prevActiveIds.Count > 0)
            await _db.Shapes.Where(s => prevActiveIds.Contains(s.FeedVersionId)).ExecuteDeleteAsync(ct);

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);

        version = (await _db.FeedVersions.Include(fv => fv.Feed).FirstOrDefaultAsync(fv => fv.Id == feedVersionId, ct))!;

        version.StopCount = rawStops.Count;
        version.RouteCount = routes.Count;
        version.TripCount = trips.Count;
        version.AgencyCount = agencies.Count;
        version.ConvexHull = _gtfs.ComputeConvexHull(rawStops);

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
        _logStore.Clear(feedVersionId);
        _logger.LogInformation("Import complete for FeedVersion {VersionId} ({RouteCount} routes, {StopCount} stops, {TripCount} trips)",
            feedVersionId, routes.Count, rawStops.Count, trips.Count);

        var deactivated = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE cs SET IsActive = 0 FROM CanonicalStations cs WHERE cs.IsActive = 1 AND cs.StationType = 'Stop' AND (EXISTS (SELECT 1 FROM CanonicalStationOperators WHERE CanonicalStationId = cs.Id AND OperatorId = @p0) OR NOT EXISTS (SELECT 1 FROM CanonicalStationOperators WHERE CanonicalStationId = cs.Id)) AND NOT EXISTS (SELECT 1 FROM RawStops rs INNER JOIN FeedVersions fv ON fv.Id = rs.FeedVersionId WHERE rs.CanonicalStationId = cs.Id AND rs.IsActive = 1 AND fv.IsActive = 1)",
            new object[] { operatorId }, ct);
        _logStore.AddEntry(feedVersionId, $"Deactivated {deactivated} orphan CanonicalStations with no stops");
        _logger.LogInformation("Deactivated {Count} orphan CanonicalStations for FeedVersion {VersionId}", deactivated, feedVersionId);

        var reactivated = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE cs SET IsActive = 1 FROM CanonicalStations cs WHERE cs.IsActive = 0 AND cs.StationType = 'Stop' AND EXISTS (SELECT 1 FROM RawStops rs INNER JOIN FeedVersions fv ON fv.Id = rs.FeedVersionId WHERE rs.CanonicalStationId = cs.Id AND rs.IsActive = 1 AND fv.IsActive = 1)",
            ct);
        if (reactivated > 0)
            _logger.LogInformation("Reactivated {Count} CanonicalStations for all operators (FeedVersion {VersionId})", reactivated, feedVersionId);

        var deactivatedRoutes = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE cr SET IsActive = 0 FROM CanonicalRoutes cr WHERE cr.IsActive = 1 AND cr.OperatorId = @p0 AND NOT EXISTS (SELECT 1 FROM Trips t INNER JOIN FeedVersions fv ON fv.Id = t.FeedVersionId WHERE t.CanonicalRouteId = cr.Id AND fv.IsActive = 1)",
            new object[] { operatorId }, ct);
        if (deactivatedRoutes > 0)
            _logger.LogInformation("Deactivated {Count} CanonicalRoutes for FeedVersion {VersionId} with no active trips", deactivatedRoutes, feedVersionId);

        var reactivatedRoutes = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE cr SET IsActive = 1 FROM CanonicalRoutes cr WHERE cr.IsActive = 0 AND cr.OperatorId = @p0 AND EXISTS (SELECT 1 FROM Trips t INNER JOIN FeedVersions fv ON fv.Id = t.FeedVersionId WHERE t.CanonicalRouteId = cr.Id AND fv.IsActive = 1)",
            new object[] { operatorId }, ct);
        if (reactivatedRoutes > 0)
            _logger.LogInformation("Reactivated {Count} CanonicalRoutes for FeedVersion {VersionId}", reactivatedRoutes, feedVersionId);

        try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp file {Path}", tempZipPath); }
    }

    private async Task HandleImportErrorAsync(int feedVersionId, Microsoft.Data.SqlClient.SqlConnection sqlConn, Microsoft.Data.SqlClient.SqlTransaction sqlTx, Exception ex, string tempZipPath, CancellationToken ct)
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
    }
}
