using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using NetTopologySuite.Geometries;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Managers;

/// <summary>Tunables for the GTFS import path.</summary>
public class FeedImportOptions
{
    /// <summary>
    /// Command timeout for the bulk import statements. A large feed's StopTimes backfill runs far
    /// past the 30 s default.
    /// </summary>
    public int BulkCommandTimeoutSeconds { get; set; } = 600;
}

public class FeedManager
{
    private readonly TransitDbContext _db;
    private readonly ILogger<FeedManager> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly Services.GtfsParser _gtfs;
    private readonly OnestopIdManager _onestopId;
    private readonly ReconciliationManager _reconciliation;
    private readonly PlaceMatchingManager _placeMatching;
    private readonly Services.ImportLogStore _logStore;
    private readonly TransitSourceResolver _sources;
    private readonly int _bulkCommandTimeoutSeconds;
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _feedLocks = new();

    /// <summary>
    /// Imports currently running, keyed by feed version. Deduplicates concurrent callers — see
    /// <see cref="ImportFeedVersionAsync"/>.
    /// </summary>
    private static readonly ConcurrentDictionary<int, Lazy<Task>> _importsInFlight = new();

    public FeedManager(
        TransitDbContext db,
        ILogger<FeedManager> logger,
        IWebHostEnvironment env,
        Services.GtfsParser gtfs,
        OnestopIdManager onestopId,
        ReconciliationManager reconciliation,
        PlaceMatchingManager placeMatching,
        Services.ImportLogStore logStore,
        TransitSourceResolver sources,
        Microsoft.Extensions.Options.IOptions<FeedImportOptions> importOptions)
    {
        _db = db;
        _logger = logger;
        _env = env;
        _gtfs = gtfs;
        _onestopId = onestopId;
        _reconciliation = reconciliation;
        _placeMatching = placeMatching;
        _logStore = logStore;
        _sources = sources;
        _bulkCommandTimeoutSeconds = importOptions.Value.BulkCommandTimeoutSeconds;
    }

    public async Task<(List<FeedResponse> Feeds, int Total)> GetAllAsync(int page = 1, int perPage = 50, bool showInternal = false, CancellationToken ct = default)
    {
        // Projected through FeedMapper.ToResponseExpression, so an Include would be discarded.
        var query = _db.Feeds
            .AsNoTracking()
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
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(FeedMapper.ToResponseExpression)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Feed> CreateAsync(
        int operatorId, string feedType,
        string feedId, string? url, int refreshIntervalSeconds, CancellationToken ct,
        int? customSourceId = null)
    {
        if (!Enum.TryParse<FeedType>(feedType, true, out var parsedFeedType))
            throw new Exceptions.AppException($"Invalid feed type '{feedType}'.", 400, "INVALID_FEED_TYPE");

        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new Exceptions.AppException("Invalid feed URL. Must be an absolute HTTP(S) URL.", 400, "INVALID_FEED_URL");

        // A custom-source feed has no archive URL of its own — the source's own requests carry the
        // URLs — so the .zip hint and the missing-URL case are both expected there.
        if (parsedFeedType == FeedType.GTFSStatic && customSourceId is null && url is not null
            && !url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            _logger.LogWarning("Feed {FeedId} static URL does not end with .zip — may not be a valid GTFS archive", feedId);
        if (customSourceId is null && url is null && parsedFeedType == FeedType.GTFSStatic)
            throw new Exceptions.AppException(
                "A GTFS static feed needs either a URL or a custom source.", 400, "FEED_HAS_NO_SOURCE");
        if (refreshIntervalSeconds < 60)
            throw new Exceptions.AppException("Refresh interval must be at least 60 seconds.", 400, "INVALID_REFRESH_INTERVAL");

        if (customSourceId is { } sourceId && !await _db.CustomSources.AnyAsync(cs => cs.Id == sourceId, ct))
            throw new Exceptions.AppException("Custom source not found.", 404, "CUSTOM_SOURCE_NOT_FOUND");

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
            CustomSourceId = customSourceId,
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
            $"UPDATE CanonicalStations SET IsActive = 0 WHERE IsActive = 1 AND StationType = '{nameof(StationType.Stop)}' "
            + "AND NOT EXISTS (SELECT 1 FROM CanonicalStationOperators WHERE CanonicalStationId = Id) "
            + "AND NOT EXISTS (SELECT 1 FROM RawStops WHERE CanonicalStationId = Id AND IsActive = 1)", ct);

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

            // Removed: HEAD/ETag pre-check. It matched against any past FeedVersion (including
            // inactive/failed ones) and short-circuited real re-imports whenever an upstream
            // server returned a stale or unstable ETag/Last-Modified. The SHA1 check below,
            // done on full downloaded content, is the reliable "unchanged" signal — no need
            // for a second, less trustworthy one.

            var source = _sources.For(feed);
            if (source is null)
            {
                _logger.LogWarning("Feed {FeedId} has no transit source able to handle it", feed.FeedId);
                return null;
            }

            // Downloading, persisting and hashing all belong to the source: what counts as "the
            // content" differs per kind, and a custom source has no single archive to SHA-1.
            var fetched = await source.FetchAsync(feed, ct);
            if (fetched is null) return null;

            var sha1 = fetched.ContentHash;

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

                // Left null deliberately. These were populated from the HEAD pre-check removed
                // above, and have been written as null on every fetch since; the SHA-1 is the
                // change signal now.
                LastModified = null,
                ETag = null
            };

            _db.FeedVersions.Add(version);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("New FeedVersion {VersionId} for feed {FeedId} SHA1={Sha1}", version.Id, feed.FeedId, sha1);
            _logStore.AddEntry(version.Id, $"Downloaded {fetched.ByteCount:N0} bytes via {source.Kind}");
            _logStore.AddEntry(version.Id, $"SHA1 = {sha1}");
            return version;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Imports a feed version, joining an import of the same version that is already running rather
    /// than starting a second one.
    /// <para>
    /// The per-feed semaphore below serialises concurrent callers but does not deduplicate them:
    /// the poller's scheduled run and an admin pressing "fetch and import" would queue up and each
    /// do the work in turn. The second pass re-runs the reconciliation backfill, an UPDATE over
    /// every StopTime in the feed that takes minutes on ZET — and for its duration the previous
    /// pass's <c>CanonicalStationId</c> values are blanked, so departures return nothing for a feed
    /// that was working seconds earlier.
    /// </para>
    /// </summary>
    public async Task ImportFeedVersionAsync(int feedVersionId, CancellationToken ct = default)
    {
        var inFlight = _importsInFlight.GetOrAdd(
            feedVersionId,
            id => new Lazy<Task>(() => ImportFeedVersionCoreAsync(id, ct), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            await inFlight.Value;
        }
        finally
        {
            // Removed by key *and value*, so a caller finishing late cannot evict the entry a newer
            // import has already installed for the same version.
            if (inFlight.IsValueCreated && inFlight.Value.IsCompleted)
                _importsInFlight.TryRemove(new KeyValuePair<int, Lazy<Task>>(feedVersionId, inFlight));
        }
    }

    // Single bad record fails the entire import. Acceptable for current feed quality (ZET, HZPP).
    // Revisit for noisier Phase 2 feeds.
    private async Task ImportFeedVersionCoreAsync(int feedVersionId, CancellationToken ct)
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

            var source = _sources.For(version.Feed);
            if (source is null)
            {
                version.ImportStatus = FeedImportStatus.Failed;
                version.ImportError = "No transit source is able to read this feed.";
                _logStore.AddEntry(feedVersionId, $"Error: {version.ImportError}");
                await _db.SaveChangesAsync(ct);
                return;
            }

            version.ImportStatus = FeedImportStatus.Importing;
            await _db.SaveChangesAsync(ct);
            _logStore.AddEntry(feedVersionId, "Import started");

            // Scoped so the source releases its archive handle and temp copy before reconciliation
            // runs — reconciliation is the long pass, and there is nothing left to read by then.
            await using (var read = await source.OpenAsync(version.Feed, feedVersionId, ct))
            {
                if (read.Failed)
                {
                    // Reading now happens before the cleanup DELETEs rather than after, so a feed
                    // that fails to parse leaves the previous rows for this version untouched
                    // instead of emptying them first.
                    version.ImportStatus = FeedImportStatus.Failed;
                    version.ImportError = string.Join("; ", read.Errors);
                    _logStore.AddEntry(feedVersionId, $"Error: {version.ImportError}");
                    await _db.SaveChangesAsync(ct);
                    return;
                }

                var document = read.Document!;

                var (sqlConn, sqlTx, ownsTransaction) = await BeginImportTransactionAsync(ct);
                try
                {
                    _db.Database.SetCommandTimeout(_bulkCommandTimeoutSeconds);
                    _db.ChangeTracker.AutoDetectChangesEnabled = false;
                    try
                    {
                        await CleanupExistingDataAsync(feedVersionId, ct);

                        _db.ChangeTracker.Clear();

                        // Each phase is skipped when its section is absent — a null section means the
                        // source does not carry it, which is a fact about the operator rather than a
                        // defect in the feed. A GTFS archive always populates all of them.
                        if (document.HasStopTimes)
                            await AutoGenerateShapesIfMissing(feedVersionId, document, read.StopTimes!, ct);

                        var carryForwardShapeIds = document.HasStopTimes
                            ? await CarryForwardManualEditsAsync(feedVersionId, version, document, read.StopTimes!, ct)
                            : [];

                        var canonicalRouteLookup = document.Routes is not null
                            ? await ImportRoutesPhaseAsync(feedVersionId, version, document.Routes, document.Stops ?? [], document.OperatorId, ct)
                            : [];

                        var prefix = $"gt-{version.Feed.FeedId}-";
                        var tripLookup = document.Trips is not null
                            ? await ImportTripsShapesCalendarsPhaseAsync(feedVersionId, version, prefix, document.Trips, document.Shapes, document.Calendar ?? [], document.CalendarDates ?? [], canonicalRouteLookup, carryForwardShapeIds, ct)
                            : [];

                        await BackfillRouteGeometriesAsync(feedVersionId, canonicalRouteLookup, ct);

                        var routeTypesPerStop = document.HasStopTimes
                            ? await ImportStopTimesBulkPhaseAsync(feedVersionId, read.StopTimes!, tripLookup, document.RouteByTrip, document.RouteTypeByRoute, sqlConn, sqlTx, ct)
                            : [];

                        await ImportAgenciesAndStopsPhaseAsync(feedVersionId, document.Agencies ?? [], document.Stops ?? [], routeTypesPerStop, document.OperatorId, document.DefaultRouteType, ct);

                        // Reconciliation deliberately does NOT run here. It used to, which meant the
                        // import transaction — holding write locks on Trips, StopTimes, Shapes,
                        // Calendars, RawStops and Agencies, escalated to table locks at this row count —
                        // stayed open across the whole matching pass and a backfill UPDATE budgeted at
                        // 600 seconds. Three feeds import in parallel and reconciliation writes the
                        // *shared* CanonicalStations and CanonicalStationOperators tables, so that was a
                        // blocking and deadlock risk between imports.
                        //
                        // It now runs after the commit below. See ReconcileImportedVersionAsync for what
                        // happens when it fails.
                        await FinalizeVersionPhaseAsync(feedVersionId, version, document, sqlTx, ct);
                    }
                    finally
                    {
                        _db.ChangeTracker.AutoDetectChangesEnabled = true;
                        _db.Database.SetCommandTimeout(30);
                    }
                }
                catch (Exception ex)
                {
                    await HandleImportErrorAsync(feedVersionId, sqlConn, sqlTx, ex, ct);
                    throw;
                }
                finally
                {
                    // Covers the early returns inside the try as well — a phase that commits and
                    // returns without reaching the end of the block still has to detach.
                    EndImportTransaction(sqlTx, ownsTransaction, feedVersionId);
                }
            }

            // Everything past this point runs against committed data, outside any transaction. A
            // failure here leaves the imported version in place rather than discarding it.
            if (!await ReconcileImportedVersionAsync(feedVersionId, ct))
                return;

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

            var zipPath = GetFeedZipPath(feed.FeedId);
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

    /// <summary>
    /// Every feed the static-import poller should visit: GTFS archives, plus feeds backed by a
    /// custom source. Realtime and mobility feeds have their own workers and are excluded here.
    /// </summary>
    public async Task<List<Feed>> GetActiveImportableFeedsAsync(CancellationToken ct)
    {
        return await _db.Feeds
            .Where(f => f.IsActive
                && ((f.FeedType == FeedType.GTFSStatic && f.Url != null)
                    || f.CustomSourceId != null))
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

    /// <summary>
    /// Resolves a feed's archive path. <see cref="Feed.FeedId"/> is caller-supplied, so the containment
    /// check in <see cref="Services.FeedStorage"/> is defence in depth behind the character restriction
    /// on <c>CreateFeedRequest.FeedId</c>.
    /// </summary>
    private string GetFeedZipPath(string feedId) =>
        new Services.FeedStorage(_env.ContentRootPath).ZipPathFor(feedId);

    /// <summary>
    /// Opens the import transaction, or adopts one already in flight on this context.
    /// </summary>
    /// <returns>
    /// <c>Owned</c> is false when an existing transaction was adopted — the caller must not dispose
    /// or detach one it did not start.
    /// </returns>
    private async Task<(Microsoft.Data.SqlClient.SqlConnection SqlConn, Microsoft.Data.SqlClient.SqlTransaction SqlTx, bool Owned)> BeginImportTransactionAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
        var sqlConn = (Microsoft.Data.SqlClient.SqlConnection)conn;
        var existingTx = _db.Database.CurrentTransaction;
        if (existingTx is not null)
            return (sqlConn, (Microsoft.Data.SqlClient.SqlTransaction)existingTx.GetDbTransaction(), false);

        var sqlTx = sqlConn.BeginTransaction();
        _db.Database.UseTransaction(sqlTx);
        return (sqlConn, sqlTx, true);
    }

    /// <summary>
    /// Detaches the import transaction from the context and disposes it.
    /// <para>
    /// This has to happen on every exit from <see cref="ImportFeedVersionAsync"/>, not just the
    /// happy path. The transaction is committed by <see cref="FinalizeVersionPhaseAsync"/> — or by
    /// the validation/parse failure paths, which commit and return early — but committing the raw
    /// <c>SqlTransaction</c> does not tell EF anything, so <c>Database.CurrentTransaction</c> went on
    /// pointing at a completed transaction. The statements Finalize runs after its own commit then
    /// carried a stale transaction on the command, and the next import through the same scoped
    /// context would adopt it via the branch above rather than starting a fresh one.
    /// </para>
    /// </summary>
    private void EndImportTransaction(Microsoft.Data.SqlClient.SqlTransaction sqlTx, bool owned, int feedVersionId)
    {
        if (!owned) return;

        try
        {
            _db.Database.UseTransaction(null);
        }
        catch (Exception ex)
        {
            // Never let cleanup replace the failure that brought us here.
            _logger.LogWarning(ex, "Could not detach the import transaction for FeedVersion {VersionId}", feedVersionId);
        }

        try
        {
            sqlTx.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not dispose the import transaction for FeedVersion {VersionId}", feedVersionId);
        }
    }

    /// <summary>
    /// Clears whatever this feed version already wrote, so a re-import replaces rather than
    /// duplicates. Format validation used to live here; it belongs to the source now, and runs
    /// before this does.
    /// </summary>
    private async Task CleanupExistingDataAsync(int feedVersionId, CancellationToken ct)
    {
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
    }

    private async Task AutoGenerateShapesIfMissing(int feedVersionId, TransitDocument document, Func<IAsyncEnumerable<List<RawStopTimeRecord>>> stopTimes, CancellationToken ct)
    {
        if (document.Shapes.Count > 0) return;
        if (document.Trips is null || document.Stops is null) return;

        _logStore.AddEntry(feedVersionId, "No shapes.txt found — generating from stop_times...");

        // Streamed in batches rather than read whole. This called the unbatched ParseStopTimes,
        // which does .ToList() over the largest file in a GTFS archive — ZET's is around a million
        // rows — while ParseStopTimesBatchedAsync already existed and is what the import phase uses.
        // The grouping below still holds one entry per row, but the parser no longer materialises a
        // second full copy alongside it.
        var byTrip = new Dictionary<string, List<RawStopTimeRecord>>(StringComparer.OrdinalIgnoreCase);
        await foreach (var batch in stopTimes().WithCancellation(ct))
        {
            foreach (var st in batch)
            {
                if (!byTrip.TryGetValue(st.TripId, out var list))
                    byTrip[st.TripId] = list = [];
                list.Add(st);
            }
        }

        foreach (var list in byTrip.Values)
            list.Sort((a, b) => a.StopSequence.CompareTo(b.StopSequence));

        var stopsByStopId = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in document.Stops)
            stopsByStopId[s.StopId] = (s.StopLat, s.StopLon);

        var generatedCount = 0;

        foreach (var trip in document.Trips)
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
            if (!document.Shapes.ContainsKey(shapeId))
                document.Shapes[shapeId] = GeometryFactory.CreateLineString(coords.ToArray());
            trip.ShapeId = shapeId;
            generatedCount++;
        }

        if (generatedCount > 0)
            document.SynthesizedSections.Add("shapes");

        _logStore.AddEntry(feedVersionId,
            $"Generated {generatedCount} shapes from {document.Shapes.Count} unique paths for {document.Trips.Count} trips");
    }

    private async Task<HashSet<string>> CarryForwardManualEditsAsync(int feedVersionId, FeedVersion version, TransitDocument document, Func<IAsyncEnumerable<List<RawStopTimeRecord>>> stopTimes, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (document.Trips is null) return result;

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

        // Streamed rather than materialised: this is the third pass over stop_times in one import,
        // and only the per-trip stop id order is needed out of it.
        var newSequences = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await foreach (var batch in stopTimes().WithCancellation(ct))
        {
            foreach (var st in batch)
            {
                if (!newSequences.TryGetValue(st.TripId, out var list))
                    newSequences[st.TripId] = list = [];
                list.Add(st.StopId);
            }
        }

        var shapeIdSet = new HashSet<string>(document.Shapes.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var oldShape in oldEditedShapes)
        {
            if (!shapeIdSet.Contains(oldShape.ShapeId))
                continue;

            if (!oldSequences.TryGetValue(oldShape.ShapeId, out var oldByTrip))
                continue;

            var newTrips = document.Trips.Where(t =>
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
                document.Shapes[oldShape.ShapeId] = oldShape.Geometry;
            }
        }

        if (result.Count > 0)
            _logStore.AddEntry(feedVersionId, $"Carried forward {result.Count} manually-edited shape(s) from previous feed version");

        return result;
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
                // Stamped so the deactivation sweep can tell a route this import just wrote from one
                // left behind by an earlier feed. Without it a feed carrying routes but no timetable
                // has every route switched off moments after creating it.
                cr.LastSeenFeedVersionId = feedVersionId;

                foreach (var routeId in routeIds)
                    result[prefix + routeId.ToLowerInvariant()] = cr.Id;
            }
        }

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);

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
                FeedVersionId = feedVersionId,
                ServiceId = c.ServiceId,
                Monday = c.Monday == 1,
                Tuesday = c.Tuesday == 1,
                Wednesday = c.Wednesday == 1,
                Thursday = c.Thursday == 1,
                Friday = c.Friday == 1,
                Saturday = c.Saturday == 1,
                Sunday = c.Sunday == 1,
                StartDate = DateOnly.ParseExact(c.StartDate, "yyyyMMdd", CultureInfo.InvariantCulture),
                EndDate = DateOnly.ParseExact(c.EndDate, "yyyyMMdd", CultureInfo.InvariantCulture)
            });

        foreach (var cd in calendarDates)
        {
            if (cd.ExceptionType < 1 || cd.ExceptionType > 2)
            {
                _logger.LogWarning("Skipping calendar_date with invalid exception_type {Type} for service {ServiceId}", cd.ExceptionType, cd.ServiceId);
                continue;
            }
            // NOTE: ParseExact throws, unlike everything around it. The guard directly above skips a
            // bad exception_type with a warning, ParseStops drops stops with impossible coordinates
            // and counts them, and ParseGtfsTimeToSeconds returns null so the stop_time is skipped —
            // the pipeline's whole convention for malformed operator data is skip-and-log. One
            // unparseable date in calendar_dates.txt instead throws, which HandleImportErrorAsync
            // turns into a failed import: the entire feed is rejected over one row.
            //
            // Left as-is because the alternative is not obviously better. Dropping a calendar_date
            // silently loses a service *exception*, and the ones that matter most say "this service
            // does NOT run today" — so a skipped row shows departures for a service that is not
            // running, which is worse than showing none. Changing it means deciding that explicitly,
            // and probably surfacing the count the way droppedStops already is.
            _db.CalendarDates.Add(new CalendarDate
            {
                FeedVersionId = feedVersionId,
                ServiceId = cd.ServiceId,
                Date = DateOnly.ParseExact(cd.Date, "yyyyMMdd", CultureInfo.InvariantCulture),
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

    private async Task<Dictionary<string, RouteType>> ImportStopTimesBulkPhaseAsync(int feedVersionId, Func<IAsyncEnumerable<List<RawStopTimeRecord>>> stopTimes, Dictionary<string, int> tripLookup, Dictionary<string, string> routeByTrip, Dictionary<string, RouteType> routeTypeByRoute, Microsoft.Data.SqlClient.SqlConnection sqlConn, Microsoft.Data.SqlClient.SqlTransaction sqlTx, CancellationToken ct)
    {
        _logStore.AddEntry(feedVersionId, "Phase 3: Bulk importing stop_times...");
        var routeTypesPerStop = new Dictionary<string, RouteType>(StringComparer.OrdinalIgnoreCase);

        using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(sqlConn, Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default, sqlTx)
        {
            DestinationTableName = "StopTimes",
            BatchSize = 50000,

            // The configured timeout, not a hardcoded 180. FeedImport:BulkCommandTimeoutSeconds
            // exists because "bulk statements far exceed the 30 s default", and every other
            // long-running step of the import honours it — but this one, the longest of them and the
            // one that streams millions of stop_times rows, quietly used its own smaller number. So
            // raising the setting to get a big feed through had no effect on the operation most
            // likely to need it.
            BulkCopyTimeout = _bulkCommandTimeoutSeconds
        };

        // Mapped by source *ordinal*, not by source name. ObjectArrayReader below is positional —
        // it has no column names and throws NotSupportedException from GetOrdinal/GetName. Naming
        // the source column makes SqlBulkCopy resolve it through GetOrdinal, so every call to
        // WriteToServerAsync threw and no GTFS static feed could complete phase 3 at all.
        // The order here must match the row array built below.
        bulkCopy.ColumnMappings.Add(0, "TripId");
        bulkCopy.ColumnMappings.Add(1, "RawStopId");
        bulkCopy.ColumnMappings.Add(2, "ArrivalTime");
        bulkCopy.ColumnMappings.Add(3, "DepartureTime");
        bulkCopy.ColumnMappings.Add(4, "StopSequence");
        bulkCopy.ColumnMappings.Add(5, "StopHeadsign");
        bulkCopy.ColumnMappings.Add(6, "PickupType");
        bulkCopy.ColumnMappings.Add(7, "DropOffType");
        bulkCopy.ColumnMappings.Add(8, "Timepoint");

        await foreach (var batch in stopTimes().WithCancellation(ct))
        {
            var rows = new List<object?[]>(50000);
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

                object?[] row =
                [
                    resolvedTripId, st.StopId,
                    (object?)arrival ?? DBNull.Value,
                    departure.Value,
                    st.StopSequence, (object?)st.StopHeadsign ?? DBNull.Value,
                    pickupType, dropOffType,
                    st.Timepoint.HasValue ? (object)(st.Timepoint == 1) : DBNull.Value,
                ];
                rows.Add(row);
            }
            await bulkCopy.WriteToServerAsync(new ObjectArrayReader(rows, 9), ct);
        }

        _logStore.AddEntry(feedVersionId, "Phase 3: stop_times import complete");
        return routeTypesPerStop;
    }

    private async Task ImportAgenciesAndStopsPhaseAsync(int feedVersionId, List<RawAgencyRecord> agencies, List<RawStopRecord> rawStops, Dictionary<string, RouteType> routeTypesPerStop, int operatorId, RouteType? defaultRouteType, CancellationToken ct)
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
                // Falls back to the feed's own mode when no stop time reaches this stop.
                // Reconciliation parks a stop with no route type as Inactive, which for a feed
                // carrying no timetable at all would be every one of them.
                RouteType = routeTypesPerStop.TryGetValue(s.StopId, out var rt) ? rt : defaultRouteType,
                IsActive = true,
                ReconciliationStatus = ReconciliationStatus.Pending
            });
        }

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync(ct);
        _logStore.AddEntry(feedVersionId, "Phase 4: agencies and stops saved");
    }

    /// <summary>
    /// Reconciles an already-committed feed version, and records the outcome on the version itself.
    /// Returns false when reconciliation did not complete.
    /// <para>
    /// This is the half of the import that runs outside the transaction. The trade-off it makes is
    /// deliberate: a failure here can no longer roll the import back, so instead of losing the data
    /// the version is parked in <see cref="FeedImportStatus.ReconciliationPending"/> — its trips and
    /// stop times are intact, but its raw stops are not yet linked to canonical stations, so nothing
    /// resolves through it on the map. That state is visible in the admin console and repaired by
    /// calling this again through <c>POST /feeds/versions/{id}/reconcile</c>.
    /// </para>
    /// <para>
    /// It is idempotent: reconciliation matches raw stops by OnestopId and re-links rather than
    /// duplicating, so re-running a partially completed pass converges rather than double-writing.
    /// </para>
    /// </summary>
    public async Task<bool> ReconcileImportedVersionAsync(int feedVersionId, CancellationToken ct = default)
    {
        var version = await _db.FeedVersions
            .Include(fv => fv.Feed)
            .FirstOrDefaultAsync(fv => fv.Id == feedVersionId, ct);

        if (version is null)
            throw new Exceptions.AppException($"FeedVersion {feedVersionId} not found.", 404, "FeedVersionNotFound");

        try
        {
            _logStore.AddEntry(feedVersionId, "Reconciling stations...");
            await ReconcileAndBackfillAsync(feedVersionId, version, ct);
            _logStore.AddEntry(feedVersionId, "Reconciliation complete");

            // Only now is the version genuinely usable. Finalize already set Success; this reasserts
            // it so a repaired version leaves ReconciliationPending behind.
            version.ImportStatus = FeedImportStatus.Success;
            version.ImportError = null;
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reconciliation failed for FeedVersion {VersionId}; the imported data is kept and the version is marked {Status}",
                feedVersionId, nameof(FeedImportStatus.ReconciliationPending));
            _logStore.AddEntry(feedVersionId, $"Reconciliation failed: {ex.Message}. The imported data was kept — re-run reconciliation to repair it.");

            // Written through a separate context so it survives whatever state the failure left the
            // tracked entities in.
            try
            {
                await _db.FeedVersions
                    .Where(fv => fv.Id == feedVersionId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(fv => fv.ImportStatus, FeedImportStatus.ReconciliationPending)
                        .SetProperty(fv => fv.ImportError, ex.InnerException?.Message ?? ex.Message), ct);
            }
            catch (Exception markEx)
            {
                _logger.LogError(markEx, "Could not mark FeedVersion {VersionId} as {Status}", feedVersionId, nameof(FeedImportStatus.ReconciliationPending));
            }

            return false;
        }
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
            _db.Database.SetCommandTimeout(_bulkCommandTimeoutSeconds);
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE st SET st.RawStopEntityId = rs.Id, st.CanonicalStationId = rs.CanonicalStationId FROM StopTimes st INNER JOIN RawStops rs ON st.RawStopId = rs.RawStopId WHERE rs.FeedVersionId = {feedVersionId}", ct);
            _db.Database.SetCommandTimeout(prevTimeout);
            _logStore.AddEntry(feedVersionId, $"Backfill complete: {rows} rows updated");
        }
        catch (Exception backfillEx)
        {
            // Deliberately does not set a status here any more. This runs after the import has
            // committed, so marking the version Failed would condemn data that is present and
            // correct. ReconcileImportedVersionAsync owns the outcome and parks it in
            // ReconciliationPending instead.
            _logStore.AddEntry(feedVersionId, $"Backfill failed: {backfillEx.InnerException?.Message ?? backfillEx.Message}");
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

    private async Task FinalizeVersionPhaseAsync(int feedVersionId, FeedVersion version, TransitDocument document, Microsoft.Data.SqlClient.SqlTransaction sqlTx, CancellationToken ct)
    {
        var operatorId = document.OperatorId;
        var routes = document.Routes ?? [];
        var rawStops = document.Stops ?? [];
        var trips = document.Trips ?? [];
        var calendar = document.Calendar ?? [];
        var calendarDates = document.CalendarDates ?? [];
        var agencies = document.Agencies ?? [];

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

        // Recorded so a consumer can tell a stops-only feed from one whose timetable failed to
        // import, and so the admin console can show which sections were inferred rather than read.
        version.Completeness = document.Completeness;
        version.SynthesizedSections = document.SynthesizedSections.Count > 0
            ? string.Join(",", document.SynthesizedSections)
            : null;

        if (calendar.Count > 0)
        {
            if (DateOnly.TryParseExact(calendar.Min(c => c.StartDate), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                version.ServiceLevelStart = start;
            if (DateOnly.TryParseExact(calendar.Max(c => c.EndDate), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                version.ServiceLevelEnd = end;
        }
        else if (calendarDates.Count > 0)
        {
            if (DateOnly.TryParseExact(calendarDates.Min(cd => cd.Date), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                version.ServiceLevelStart = start;
            if (DateOnly.TryParseExact(calendarDates.Max(cd => cd.Date), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
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
            $"UPDATE cs SET IsActive = 0 FROM CanonicalStations cs WHERE cs.IsActive = 1 AND cs.StationType = '{nameof(StationType.Stop)}' AND (EXISTS (SELECT 1 FROM CanonicalStationOperators WHERE CanonicalStationId = cs.Id AND OperatorId = @p0) OR NOT EXISTS (SELECT 1 FROM CanonicalStationOperators WHERE CanonicalStationId = cs.Id)) AND NOT EXISTS (SELECT 1 FROM RawStops rs INNER JOIN FeedVersions fv ON fv.Id = rs.FeedVersionId WHERE rs.CanonicalStationId = cs.Id AND rs.IsActive = 1 AND fv.IsActive = 1)",
            new object[] { operatorId }, ct);
        _logStore.AddEntry(feedVersionId, $"Deactivated {deactivated} orphan CanonicalStations with no stops");
        _logger.LogInformation("Deactivated {Count} orphan CanonicalStations for FeedVersion {VersionId}", deactivated, feedVersionId);

        var reactivated = await _db.Database.ExecuteSqlRawAsync(
            $"UPDATE cs SET IsActive = 1 FROM CanonicalStations cs WHERE cs.IsActive = 0 AND cs.StationType = '{nameof(StationType.Stop)}' AND EXISTS (SELECT 1 FROM RawStops rs INNER JOIN FeedVersions fv ON fv.Id = rs.FeedVersionId WHERE rs.CanonicalStationId = cs.Id AND rs.IsActive = 1 AND fv.IsActive = 1)",
            ct);
        if (reactivated > 0)
            _logger.LogInformation("Reactivated {Count} CanonicalStations for all operators (FeedVersion {VersionId})", reactivated, feedVersionId);

        // A route stays active while it has trips in an active version, OR while an active version
        // still lists it. The second clause is what keeps a Network-completeness feed usable: its
        // routes are real and current, they simply have no timetable attached, and judging them
        // solely by trip count switched off everything such a feed had just imported.
        var deactivatedRoutes = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE cr SET IsActive = 0 FROM CanonicalRoutes cr WHERE cr.IsActive = 1 AND cr.OperatorId = @p0 "
            + "AND NOT EXISTS (SELECT 1 FROM Trips t INNER JOIN FeedVersions fv ON fv.Id = t.FeedVersionId WHERE t.CanonicalRouteId = cr.Id AND fv.IsActive = 1) "
            + "AND NOT EXISTS (SELECT 1 FROM FeedVersions fv2 WHERE fv2.Id = cr.LastSeenFeedVersionId AND fv2.IsActive = 1)",
            new object[] { operatorId }, ct);
        if (deactivatedRoutes > 0)
            _logger.LogInformation("Deactivated {Count} CanonicalRoutes for FeedVersion {VersionId} with no active trips", deactivatedRoutes, feedVersionId);

        var reactivatedRoutes = await _db.Database.ExecuteSqlRawAsync(
            "UPDATE cr SET IsActive = 1 FROM CanonicalRoutes cr WHERE cr.IsActive = 0 AND cr.OperatorId = @p0 "
            + "AND (EXISTS (SELECT 1 FROM Trips t INNER JOIN FeedVersions fv ON fv.Id = t.FeedVersionId WHERE t.CanonicalRouteId = cr.Id AND fv.IsActive = 1) "
            + "OR EXISTS (SELECT 1 FROM FeedVersions fv2 WHERE fv2.Id = cr.LastSeenFeedVersionId AND fv2.IsActive = 1))",
            new object[] { operatorId }, ct);
        if (reactivatedRoutes > 0)
            _logger.LogInformation("Reactivated {Count} CanonicalRoutes for FeedVersion {VersionId}", reactivatedRoutes, feedVersionId);
    }

    private async Task HandleImportErrorAsync(int feedVersionId, Microsoft.Data.SqlClient.SqlConnection sqlConn, Microsoft.Data.SqlClient.SqlTransaction sqlTx, Exception ex, CancellationToken ct)
    {
        try { using var rollbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token); await sqlTx.RollbackAsync(rollbackCts.Token); } catch (Exception rollbackEx) { _logger.LogWarning(rollbackEx, "Rollback failed for FeedVersion {VersionId}", feedVersionId); }
        _logStore.AddEntry(feedVersionId, $"Import failed: {ex.Message}");
        try
        {
            using var errConn = new Microsoft.Data.SqlClient.SqlConnection(sqlConn.ConnectionString);
            await errConn.OpenAsync(ct);
            await using var cmd = errConn.CreateCommand();
            cmd.CommandText = "UPDATE FeedVersions SET ImportStatus = @status, ImportError = @error WHERE Id = @id";

            // The enum NAME, not its ordinal. TransitDbContext applies EnumToStringConverter to every
            // enum property in the model, so ImportStatus is an nvarchar holding "Failed" — and this
            // raw command bypasses EF entirely, so nothing was applying that conversion. Passing
            // (int)FeedImportStatus.Failed made SQL Server implicitly convert it and store the string
            // "3", which no EF query could find: every `Where(fv => fv.ImportStatus ==
            // FeedImportStatus.Failed)` compiles to `WHERE ImportStatus = N'Failed'` and matched
            // none of the rows this method had written. Failed imports were invisible to the admin
            // console's status filter for exactly that reason.
            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@status", nameof(FeedImportStatus.Failed)));
            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@error", ex.InnerException?.Message ?? ex.Message));
            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@id", feedVersionId));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception innerEx)
        {
            _logger.LogError(innerEx, "Failed to write ImportStatus=Failed for FeedVersion {VersionId}", feedVersionId);
        }
        _logger.LogError(ex, "Import failed for FeedVersion {VersionId}", feedVersionId);

        // Released here as well as on the success path. Clear() was only called by
        // FinalizeVersionPhaseAsync, so every failed import kept its log list in memory for the
        // lifetime of the process. The entries have already been written to the logger above.
        _logStore.Clear(feedVersionId);
    }

    private sealed class ObjectArrayReader(IEnumerable<object?[]> rows, int fieldCount) : IDataReader
    {
        private readonly IEnumerator<object?[]> _enumerator = rows.GetEnumerator();

        public int FieldCount => fieldCount;
        public int RecordsAffected => -1;
        public bool IsClosed => false;
        public int Depth => 0;

        public bool Read() => _enumerator.MoveNext();
        public object GetValue(int i) => _enumerator.Current![i] ?? DBNull.Value;
        public bool IsDBNull(int i) => _enumerator.Current![i] is null;
        public object this[int i] => GetValue(i);
        public object this[string name] => throw new NotSupportedException();
        public int GetOrdinal(string name) => throw new NotSupportedException();
        public string GetName(int i) => throw new NotSupportedException();
        public string GetDataTypeName(int i) => throw new NotSupportedException();
        public Type GetFieldType(int i) => throw new NotSupportedException();
        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public int GetValues(object[] values) => throw new NotSupportedException();
        public bool GetBoolean(int i) => throw new NotSupportedException();
        public byte GetByte(int i) => throw new NotSupportedException();
        public char GetChar(int i) => throw new NotSupportedException();
        public DateTime GetDateTime(int i) => throw new NotSupportedException();
        public decimal GetDecimal(int i) => throw new NotSupportedException();
        public double GetDouble(int i) => throw new NotSupportedException();
        public float GetFloat(int i) => throw new NotSupportedException();
        public Guid GetGuid(int i) => throw new NotSupportedException();
        public short GetInt16(int i) => throw new NotSupportedException();
        public int GetInt32(int i) => throw new NotSupportedException();
        public long GetInt64(int i) => throw new NotSupportedException();
        public string GetString(int i) => throw new NotSupportedException();
        public DataTable GetSchemaTable() => throw new NotSupportedException();
        public void Close() { }
        public void Dispose() => _enumerator.Dispose();
        public bool NextResult() => false;
    }
}
