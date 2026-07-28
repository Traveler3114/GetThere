# TransitInfoDb — Schema Reference

SQL Server with **NetTopologySuite** spatial types. Context:
`TransitInfoAPI.Data.TransitDbContext`, deriving from `IdentityDbContext<AppUser>`.

Two differences from GetThereDb worth knowing up front:

- **Migrations are applied automatically at startup** (`db.Database.MigrateAsync()`), followed by a
  crash-recovery sweep for interrupted imports.
- **Spatial columns are real geometry**, enabled by `UseNetTopologySuite()`, with a 120-second command
  timeout on the connection because bulk import statements far exceed the default.

There is currently **one migration**: `20260722145915_InitialCreate`. The schema was rebaselined —
see [../../transitinfodb-rebaseline.md](../../transitinfodb-rebaseline.md).

---

## Global conventions

Both applied by a loop over the whole model in `OnModelCreating`:

**Enums as strings**, with `SetMaxLength(50)`. Critical here because `RouteType` uses GTFS's own sparse
numbering — `Tram = 0`, `Trolleybus = 11`, `Bicycle = 100`, `Airplane = 200`. Feed data outlives code,
and string storage means a renumbering cannot silently reinterpret existing rows.

**Every FK is `Restrict`**:

```csharp
// Disable cascade deletes globally — SQL Server doesn't allow multiple cascade paths
```

That is not a stylistic preference: this graph genuinely has multiple paths — `FeedVersion` reaches
`CanonicalStation` through both `RawStop` and `StopTime` — and SQL Server rejects that. The
consequence is that `FeedManager.DeleteAsync` is a hand-written ordered walk.

Three deliberate overrides:

| FK | Behaviour | Why |
|---|---|---|
| `RefreshTokens.UserId` | `Cascade` | Tokens are worthless without their user |
| `StationMergeLog` → both stations | `NoAction` | Must reference a station that has since been deactivated |
| `StationMergeMovedRawStop` → log | `Cascade` | Pure child rows of the log |

---

## The central model

```
Operator ──┬── Feed ──── FeedVersion ──┬── Agency
           │                           ├── RawStop ──────┐
           │                           ├── Trip ── StopTime
           │                           ├── Calendar / CalendarDate
           │                           └── Shape          │ reconciliation
           ├── CanonicalRoute                             ▼
           └── CanonicalStationOperator ─── CanonicalStation ─── Place
                                                  │
                                            Country ── City
```

The raw/canonical split is the whole design; see
[../transitinfo-api/reconciliation.md](../transitinfo-api/reconciliation.md).

---

## Canonical entities

These are stable across feeds, operators and re-imports. All carry a **unique `OnestopId`**, which is
what external systems store — GetThereAPI holds one on every imported ticket.

### `Operators`

| Column | Notes |
|---|---|
| `Id` | PK |
| `GlobalId` | **The identifier GetThereAPI stores** in `TicketingAdapter.TransitInfoGlobalId` |
| `OnestopId` | **Unique** |
| `Name`, `ShortName`, `Website` | |
| `SupersedesIds` | Legacy ID migration **only** — not a merge record |
| `WikidataId`, `Tags`, `AssociatedFeeds` | Enrichment |
| `CreatedAt` | |

### `CanonicalStations`

| Column | Notes |
|---|---|
| `Id` | PK |
| `OnestopId` | **Unique**. `s-{geohash9}-{slug}~{routetype}` |
| `Name` | |
| `Latitude`, `Longitude` | `float` — used for arithmetic |
| `Geometry` | `Point` SRID 4326 — used for spatial queries |
| `StationType` | `Station`, `Airport`, `Port`, `BusTerminal`, `FerryTerminal`, `Stop`, `Platform`, `Unknown` |
| `PrimaryRouteType` | Must match for a merge to be allowed |
| `IsActive` | **Deactivation is how stations are retired — never deletion** |
| `SupersedesIds` | Legacy IDs only |
| `AdmCountryCode`, `AdmRegionCode` | |
| `CountryId` | FK, required |
| `CityId`, `PlaceId` | FK, optional |

Coordinates are stored **twice**, as scalars and as geometry. Redundant, and deliberate: the
reconciliation hot path does millions of distance calculations in C# where plain doubles are far
cheaper, while spatial queries need real geometry.

`IsActive = false` rather than deletion is what keeps external references valid. A ticket in someone's
wallet may name this OnestopId.

> ```
> // Decision (Task 4.17): Stations relate to each other in exactly two ways:
> //   - Fully merged (source deactivated, recorded in StationMergeLog)
> //   - Fully independent
> // No "related but separate" linking concept exists.
> ```
> There is no `parent_station` hierarchy, despite GTFS having one. The binary model keeps the
> invariant simple: a raw stop belongs to exactly one canonical station.

### `CanonicalRoutes`

`Id`, `OnestopId` (unique), `ShortName`, `LongName`, `RouteType`, `Color`, `TextColor`, `IsActive`,
`SupersedesIds`, `Geometry` (`LineString`), `ShapeEdited`, `OperatorId` (FK, indexed).

**`ShapeEdited` is the flag that protects manual work.** `PUT /routes/{id}/shape` sets it, and
`CarryForwardManualEditsAsync` reads it on the next import to preserve the hand-drawn geometry instead
of overwriting it with the feed's.

### `CanonicalStationOperators`

Join table. **Composite PK `(CanonicalStationId, OperatorId)`**, with a separate index on `OperatorId`
for the reverse lookup.

This is the many-to-many that makes the model work: one canonical station is served by several
operators, which is the whole point of reconciliation.

---

## Feed entities

### `Feeds`

| Column | Notes |
|---|---|
| `Id` | PK |
| `OnestopId` | |
| `FeedId` | **Unique.** Used as an on-disk directory name — path-containment checked |
| `FeedType` | `GTFSStatic`, `GTFSRealtime`, `GBFS` |
| `Url` | Validated absolute HTTP(S); SSRF-checked at fetch |
| `IsActive` | Cleared by auto-deactivation after repeated failures |
| `IsInternal` | Source fetches **and** imports itself; hidden from `GET /feeds` by default |
| `RefreshIntervalSeconds` | ≥ 60 enforced |
| `License*` (5 columns) | **Recorded, never enforced** |
| `OperatorId` | FK |

### `FeedVersions`

| Column | Notes |
|---|---|
| `Id` | PK |
| `FeedId` | FK |
| `Sha1` | **Unique.** SHA-1 of full content — the only "unchanged" signal |
| `FetchedAt`, `ImportedAt` | |
| `IsActive` | See the filtered index below |
| `ImportStatus` | `Pending`, `Importing`, `Success`, `Failed`, `Skipped` |
| `ImportError` | |
| `LastModified`, `ETag` | Diagnostics only — **they gate nothing** |
| `ConvexHull` | `Geometry` — the feed's spatial extent |
| `ServiceLevelStart/End` | `date` |
| `StopCount`, `RouteCount`, `TripCount`, `AgencyCount` | Denormalised |

```sql
CREATE UNIQUE INDEX IX_FeedVersions_FeedId_IsActive
ON FeedVersions (FeedId, IsActive) WHERE [IsActive] = 1;
```

The filter is what makes immutable versioning work: any number of inactive versions may coexist, but
**the database guarantees a feed can never have two active ones.**

`ImportStatus = 'Importing'` at startup means the process died mid-import; startup marks it `Failed`
and deletes its partial rows.

### `RawStops`

What one feed version literally said.

| Column | Notes |
|---|---|
| `Id` | PK |
| `FeedVersionId` | FK |
| `RawStopId` | The feed's own id. **Unique with `FeedVersionId`** |
| `Name`, `Lat`, `Lon` | |
| `StationType` | |
| `ParentRawStopId` | GTFS `parent_station` — **imported but not modelled as a relation** |
| `StopCode`, `StopDesc`, `ZoneId`, `PlatformCode` | |
| `WheelchairBoarding` | |
| `RouteType` | Nullable. Derived from the trips serving the stop |
| `IsActive` | |
| `CanonicalStationId` | FK, nullable, **indexed** — the reconciliation link |
| `ReconciliationStatus` | How it got there |

**`RouteType` being null means the stop is skipped by reconciliation entirely** — it gets no canonical
station and never becomes queryable. Usually correct (a stop no trip serves is not in service), but it
is the first thing to check when a stop is unexpectedly missing.

### `Trips`, `StopTimes`, `Calendars`, `CalendarDates`, `Shapes`

`Trips` — `(FeedVersionId, TripId)` unique, `CanonicalRouteId` indexed.

**`StopTimes` is the largest table in the system.**

| Column | Notes |
|---|---|
| `Id` | PK |
| `TripId` | FK, **indexed** |
| `RawStopId` | `nvarchar(450)` — the feed's string |
| `RawStopEntityId` | FK, nullable, **indexed** — backfilled after bulk insert |
| `CanonicalStationId` | FK, nullable |
| `ArrivalTime`, `DepartureTime` | **`int` — seconds since local midnight** |
| `StopSequence` | |
| `StopHeadsign`, `PickupType`, `DropOffType`, `Timepoint` | |

Three things here are load-bearing:

```csharp
// 450 chars × 2 bytes (NVARCHAR) = 900 bytes = SQL Server non-clustered index key limit
[MaxLength(450)] public string RawStopId { get; set; }
```

**Times are integers, not a time type**, so they can exceed 86400 — GTFS's `25:30:00` means 1:30 am on
the following service day, which is how a service running past midnight stays attached to the right
day.

**The covering index** is what makes departures fast:

```csharp
.HasIndex(st => new { st.CanonicalStationId, st.DepartureTime })
.IncludeProperties(st => st.TripId);
```

Both the string and the FK exist because rows are bulk-inserted before `RawStop` and
`CanonicalStation` rows exist; the FKs are backfilled by an `UPDATE` that needs the raised command
timeout.

`Calendars` holds weekday flags plus a `DateOnly` range; `CalendarDates` holds exceptions
(`ExceptionType` 1 = added, 2 = removed). Both are queried together to decide which services run on a
given date.

`Shapes` stores a `LineString` with `IsManuallyEdited`, the shape-level counterpart to
`CanonicalRoute.ShapeEdited`.

### `Agencies`

GTFS-level, scoped to a feed version, with an optional `OperatorId` linking to the canonical operator —
the same raw/canonical split as stops.

**`Agency.Timezone` is imported and stored but never used.** `ScheduleManager` applies one configured
timezone to every feed instead, which is the documented limitation in
[../transitinfo-api/realtime.md](../transitinfo-api/realtime.md#the-timezone-limitation). The data
needed to fix it is already here.

---

## Reconciliation tables

### `ReconciliationCandidates`

| Column | Type | Notes |
|---|---|---|
| `Id` | | PK |
| `RawStopId` | | FK, **indexed** |
| `RawStopName`, `RawStopLat`, `RawStopLon` | | **Snapshotted** |
| `RawRouteType`, `CanonicalRouteType` | | |
| `NameMatched`, `DistanceMatched`, `RouteTypeMatched`, `AutoReconciled` | `bit` | Which criteria fired |
| `SuggestedCanonicalStationId` | | FK, **indexed** |
| `ConfidenceScore` | `decimal(5,4)` | |
| `DistanceMeters` | `decimal(14,4)` | |
| `NameSimilarityScore` | `decimal(5,4)` | |
| `Status` | | `Pending`, `AutoMerged`, `ManuallyApproved`, `Rejected`, `NewStation`, `Inactive` |
| `ReviewedByAdminId`, `ReviewedAt` | | |
| `FeedId` | | FK |
| `AutoMergeNameThresholdAtDecision` | `decimal(5,4)` | |
| `AutoMergeDistanceMetersAtDecision` | `decimal(14,4)` | |
| `ManualReviewNameThresholdAtDecision` | `decimal(5,4)` | |
| `ManualReviewDistanceMetersAtDecision` | `decimal(14,4)` | |

**The four `*AtDecision` columns are the most important design detail in this table.** They snapshot
the thresholds that were in force when the decision was made. Without them, changing a configured
threshold would silently rewrite the meaning of every historical decision, and an admin reviewing an
old auto-merge could not tell whether it would still be made today.

The raw stop's name and coordinates are snapshotted for the same reason: the candidate must remain
interpretable after the feed version is superseded.

`decimal(5,4)` gives scores four decimal places in `0.0000`–`9.9999`; `decimal(14,4)` gives distances
sub-millimetre precision — far beyond real accuracy, but exact so comparisons are deterministic.

### `StationMergeLogs` and `StationMergeMovedRawStops`

`StationMergeLog`: `SourceStationId`, `SourceStationGlobalId` (snapshotted), `TargetStationId`,
`RawStopsMovedCount`, `MovedRawStopIds`, `MergedAt`. Both station FKs are `NoAction` and indexed.

`StationMergeMovedRawStop`: `StationMergeLogId` (FK, `Cascade`, indexed), `RawStopId`.

**The child table is what makes unmerge possible.** After a merge, the target's raw stops are
indistinguishable by origin — there is no way to tell which came from where. The explicit list is the
only record, which is why `MovedRawStopIds` (a string) exists alongside it as a redundant human-readable
copy.

### `StationSplitLogs`

`RawStopId`, `FeedVersionId`, `CandidateStationId` (indexed), `Reason`, `Detail`, `CreatedAt`.

Records where the **importer declined to merge** and created a separate station. A diagnostic record
of an automatic decision, **not an admin action** — there is no split operation. To separate wrongly
merged stations, use unmerge or reassign.

---

## Geography and mobility

`Countries` — `Name`, `IsoCode` (**unique**), `Continent`.
`Cities` — `Name`, `Latitude`, `Longitude`, `CountryId` (indexed).
`Places` — `Name`, `AdmCountryCode`, `AdmRegionCode`, `Lat`, `Lon`, `Population`. A settlement, matched
to stations within `PlaceMatching:MaxDistanceMeters` (50 km).

`MobilityStations` — `StationId`, `Name`, `Latitude`, `Longitude`, `Capacity`, `AvailableVehicles`,
`LastUpdated`, `OperatorId` (indexed), `CountryId`. GBFS docks. Persisted, unlike vehicle positions,
because a dock's location is stable and only its count moves.

`Alerts` — `FeedId` (indexed), `HeaderText`, `DescriptionText`, `Url`, `Cause`, `Effect`,
`ActivePeriodStart/End`, `FetchedAt`, plus four **delimited string** columns:

```
AffectedStopIds, AffectedRouteIds, AffectedTripIds, AffectedAgencyIds
```

Denormalised deliberately — alerts are short-lived and rewritten wholesale each poll, so maintaining
join rows would cost more than it saves. The trade is that filtering is a `LIKE`, which uses no index.

---

## Identity and audit

Identity tables are explicitly named (`ToTable("AspNetUsers")` etc.) rather than left to convention.

`RefreshTokens` — same shape as GetThereDb's, with `Token` uniquely indexed, but **`Cascade`** on the
user FK rather than `Restrict`.

`AuditLogs` — same shape, but **`Restrict`** on the user FK rather than `SetNull`. So in this database
a user with audit history cannot be deleted at all, where in GetThereDb the history is orphaned and
kept. Two defensible choices, applied inconsistently across the two services.

---

## What is not in the database

Worth stating explicitly, because it explains restart behaviour:

| Data | Where it lives |
|---|---|
| Vehicle positions | `RealtimeManager` in-memory cache |
| Trip updates | `RealtimeManager` in-memory cache, per feed |
| Import log lines | `ImportLogStore` in-memory ring |
| GTFS archives | `{ContentRoot}/feeds/{FeedId}/gtfs.zip` on disk |

All four are lost on restart. The first three are regenerated within one polling cycle; the archives
are re-downloaded on the next poll. This is also why the service assumes a **single instance** — two
would hold divergent caches and poll independently.

---

## Operational notes

**Size.** `StopTimes` dominates — millions of rows for a national feed. `Trips` and `RawStops` are the
next largest. Everything else is small.

**Deletion.** Global `Restrict` means every delete is an explicit ordered walk. Deleting a feed
deactivates canonical stations rather than removing them.

**Spatial.** `UseNetTopologySuite()` with SRID 4326 (WGS 84). Geometry columns: `CanonicalStation.Geometry`
(Point), `CanonicalRoute.Geometry` and `Shape.Geometry` (LineString), `FeedVersion.ConvexHull`.

**Timeouts.** 120 s at the connection level, raised to `FeedImport:BulkCommandTimeoutSeconds` (600) for
the bulk import statements specifically. A large feed's `StopTimes` backfill runs far past the 30 s
default.
