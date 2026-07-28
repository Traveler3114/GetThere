# TransitInfoAPI — The GTFS Feed Pipeline

## What a feed is, and why versioning it matters

A GTFS static feed is a zip of CSV files — `stops.txt`, `routes.txt`, `trips.txt`, `stop_times.txt`,
`calendar.txt`, `shapes.txt` — published at a URL by a transit operator. Operators republish
whenever a timetable changes, which can be weekly.

The naive approach is to wipe and re-import. That is wrong here for two reasons:

1. **Reconciliation decisions would be lost.** A human may have adjudicated that ÖBB's
   `"Zagreb Hbf"` is the same station as HŽPP's `"Zagreb Glavni kolodvor"`. Wiping discards that.
2. **There would be no way to tell what changed**, or to answer "which feed version said this
   departure existed?"

So the model is **immutable versions**:

```
Feed  (the subscription: URL, operator, refresh interval, licence)
  └── FeedVersion  (one download, identified by SHA-1 of its bytes)
        ├── Agency, RawStop, Trip, StopTime, Calendar, CalendarDate, Shape
        └── IsActive — exactly one per feed, enforced by a filtered unique index
```

```csharp
modelBuilder.Entity<FeedVersion>()
    .HasIndex(fv => new { fv.FeedId, fv.IsActive })
    .IsUnique()
    .HasFilter("[IsActive] = 1");
```

The filter is what makes this work: any number of inactive versions may coexist, but the database
itself guarantees a feed can never have two active ones. `Sha1` is separately unique, so the same
bytes cannot be recorded twice.

Everything downstream reads through `IsActive`, which is why activation is the last step of an import
and why a failed import leaves the previous version serving traffic.

---

## Fetching: `CheckAndFetchAsync`

### SHA-1 of the full content is the only "unchanged" signal

There used to be a `HEAD`/`ETag` pre-check to avoid downloading unchanged feeds. It was **removed**,
and the comment explains why:

> It matched against any past FeedVersion (including inactive/failed ones) and short-circuited real
> re-imports whenever an upstream server returned a stale or unstable ETag/Last-Modified. The SHA1
> check below, done on full downloaded content, is the reliable "unchanged" signal — no need for a
> second, less trustworthy one.

This is a deliberate trade: bandwidth for correctness. Feeds are downloaded in full every poll and
discarded if the hash matches. `ETag` and `LastModified` are still *stored* on the version for
diagnostics, but they gate nothing.

### Per-feed locking

```csharp
private static readonly ConcurrentDictionary<int, SemaphoreSlim> _feedLocks = new();
```

A `static` dictionary of semaphores keyed by feed id serialises fetch and import per feed, so a manual
`POST /feeds/{id}/fetch` cannot collide with the polling worker. It is **per-process**, so it does not
protect a multi-instance deployment — consistent with `RealtimeManager`'s in-memory caches, this
service currently assumes a single instance.

`ImportFeedVersionAsync` re-reads the version *after* acquiring the lock and returns early if it is
already `Success`, because another caller may have imported it while this one waited.

### Atomic disk write

The zip is written to `gtfs.zip.tmp`, then `File.Replace`d into place (falling back to `File.Move`
when no target exists). A failed download cannot leave a truncated archive where the previous good one
was.

### Path containment

`GetFeedStorageDirectory` resolves `{ContentRoot}/feeds/{feed.FeedId}` and verifies the result stays
under the feeds root. `FeedId` is admin-supplied, so this is defence in depth behind the character
restriction on `CreateFeedRequest.FeedId`. This is the check `LocalTicketFileStore` in GetThereAPI
explicitly mirrors — a path-traversal defect here is what motivated it.

---

## Importing: `ImportFeedVersionAsync`

The import runs as a sequence of phases. Ordering is not arbitrary — each phase produces a lookup the
next one consumes.

```
1.  PrepareImportTempZip         copy to a temp file so the live zip is not held open
2.  decompression-bomb guard     reject if declared expansion > 4 GB
3.  ValidateAndCleanupAsync      validate structure, remove any partial prior attempt
4.  ParseGtfsFilesAsync          CSV → in-memory models (CsvHelper)
5.  AutoGenerateShapesIfMissing  synthesise route geometry when shapes.txt is absent
6.  CarryForwardManualEditsAsync preserve hand-edited shapes across the re-import
7.  ImportRoutesPhaseAsync       → canonicalRouteLookup
8.  ImportTripsShapesCalendars   → tripLookup
9.  BackfillRouteGeometries      derive route geometry from its trips' shapes
10. ImportStopTimesBulkPhase     SqlBulkCopy; → routeTypesPerStop
11. ImportAgenciesAndStopsPhase  RawStops, tagged with the route types serving them
12. ReconcileAndBackfillAsync    raw stops → canonical stations, then FK backfill
13. FinalizeVersionPhaseAsync    counts, convex hull, service window, activate
```

Two dependencies are worth calling out because they explain the odd ordering:

- **Stop times are imported before stops.** Phase 10 returns `routeTypesPerStop` — which route types
  actually serve each stop — and phase 11 needs it to set `RawStop.RouteType`. That field is
  load-bearing: the OnestopId includes a route-type suffix, so a tram stop and a bus stop at the same
  coordinates get different canonical identities. Without it, reconciliation would merge them.
- **Reconciliation runs before finalisation.** `IsActive` is set last, so a feed that fails
  reconciliation never becomes visible.

### Why there is no single transaction

The import opens a `SqlTransaction` for the bulk phases, but the whole import is **not** one
transaction. A national feed's `stop_times.txt` is millions of rows; holding one transaction across
parsing, bulk insert, reconciliation and geometry work would hold locks for many minutes and blow the
log.

The consequence is that a crash mid-import leaves partial data. That is handled by the **startup
recovery sweep** described in
[architecture.md](architecture.md#startup-crash-recovery): anything left `Importing` is marked
`Failed` and its rows are deleted in dependency order. The two halves are a single design, and
neither makes sense alone.

### Performance measures

| Measure | Why |
|---|---|
| `SqlBulkCopy` for stop times | EF `Add` per row is orders of magnitude too slow for millions |
| `AutoDetectChangesEnabled = false` | Change detection is O(n²)-ish on large graphs |
| `ChangeTracker.Clear()` between phases | Keeps the tracker from growing across phases |
| `BulkCommandTimeoutSeconds` = 600 | The backfill `UPDATE` far exceeds the 30 s default |
| Temp-file copy of the zip | The live `gtfs.zip` is not held open for the import's duration |

### The backfill step

`StopTime` is bulk-inserted with the feed's own `RawStopId` **string**, because the FK targets do not
exist yet — `RawStop` rows are created in phase 11 and canonical stations in phase 12. Only afterwards
can `RawStopEntityId` and `CanonicalStationId` be resolved:

```
-- This UPDATE joins StopTimes (potentially millions of rows) with RawStops
-- and may exceed the default 30s timeout. Raise it for this query only.
```

This is why the entity carries both the string and the FK, and why `RawStopId` is capped at 450
characters — 450 × 2 bytes = 900 bytes, exactly SQL Server's non-clustered index key limit.

### Decompression-bomb guard

```csharp
private const long MaxUncompressedArchiveBytes = 4L * 1024 * 1024 * 1024;
```

A small zip can declare an enormous expansion, and the parser streams entries into memory. The
declared uncompressed size is summed before any entry is opened, and the import fails cleanly rather
than exhausting memory. `ExternalFeedSource` applies a separate 512 MB ceiling on the *download*,
since feeds are buffered in memory before hashing.

The same reasoning appears in GetThereAPI's `PkPassTicketExtractor` — any archive from outside is
hostile until bounded.

### Error handling: all-or-nothing per feed

```csharp
// Single bad record fails the entire import. Acceptable for current feed quality (ZET, HZPP).
// Revisit for noisier Phase 2 feeds.
```

One malformed CSV row fails the whole import, leaving the previous version active. That is the right
default while feeds are few and well-formed: a partial import is worse than a stale one. It will not
scale to a long tail of noisy feeds, and the comment flags that.

### `ImportLogStore`

A singleton, in-memory ring of log lines per feed version, surfaced at
`GET /feeds/versions/{versionId}/logs`. It exists so an admin watching an import can see progress
without server log access. Being in-memory, **it does not survive a restart** — the durable record is
`FeedVersion.ImportStatus` and `ImportError`.

---

## Polling: `FeedPollingWorker`

Runs every `FeedPolling:IntervalMinutes` (default 60), over all active GTFS-static feeds, with
`MaxDegreeOfParallelism = 3`.

The parallelism cap is doing real work. Each import is memory- and database-heavy; running all feeds
at once would exhaust both. Three is a compromise between wall-clock time and resource pressure.

Each feed gets its own DI scope inside the loop — a `DbContext` is not thread-safe, so sharing one
across parallel iterations would corrupt state.

### Auto-deactivation

```csharp
private readonly ConcurrentDictionary<int, int> _consecutiveFailures = new();
```

After `MaxConsecutiveFailuresBeforeDeactivate` (default 10) consecutive failures, the feed is set
`IsActive = false` and the counter cleared.

The reasoning: a feed whose URL has gone permanently dead would otherwise be retried forever, filling
logs and wasting a parallelism slot every cycle. Ten consecutive failures at hourly polling is roughly
ten hours — long enough to ride out a transient outage.

**Any success resets the counter**, so intermittent failures never accumulate to deactivation. The
cost is that a permanently-dead feed goes quiet rather than loudly broken; `Feed.IsActive` is the only
signal, and re-enabling is a manual `PUT /feeds/{id}`.

Note the counter is in-process, so a restart resets it.

### Options are read live

`IOptionsMonitor<FeedPollingOptions>.CurrentValue` is re-read every cycle rather than captured in the
constructor, so a configuration change takes effect on the next cycle without a restart.

---

## Internal feeds

`Feed.IsInternal` marks a feed whose source handles fetching **and** importing itself, signalled by
`FeedFetchResult.AlreadyHandled`. The code is explicit that these must not fall through to the zip
pipeline — there is no zip on disk for them.

The `feeds/` directory shows the deployed set: `zet`, `hžpp`, `obb`, `gp`, and two
`custom-N-autotrolejstatic` directories. The `custom-` prefix and the non-ASCII `hžpp` directory name
are both worth knowing about when working with paths here.

Internal feeds are hidden from `GET /feeds` unless `showInternal=true`.

---

## Deleting a feed

`FeedManager.DeleteAsync` is the clearest illustration of why global `DeleteBehavior.Restrict` shapes
this codebase. Nothing cascades, so deletion is a hand-written ordered walk:

```
per version:  StopTimes → CalendarDates → Calendars → Shapes → Trips → RawStops → Agencies
then:         CanonicalStationOperator links with no remaining active RawStop support
then:         deactivate (not delete) CanonicalStations with no operator links and no active raw stops
then:         ReconciliationCandidates for the feed
```

The key asymmetry: **canonical stations are deactivated, never deleted.** They may be referenced by
merge logs, by another operator's raw stops, or by an external system holding their OnestopId —
GetThereAPI stores one on every imported ticket. Deleting would break those references; deactivating
preserves them while removing the station from results.

---

## Licence metadata

`Feed` carries `LicenseName`, `LicenseUrl`, `LicenseCommercialUseAllowed`, `LicenseShareAlikeOptional`
and `LicenseRedistributionAllowed`. These are **recorded but not enforced** — nothing in the code
checks them before serving data.

They exist because GTFS feeds carry real licence terms and redistributing one commercially without
checking is a genuine legal risk. Treat these fields as a compliance record to consult, not a control
that operates.
