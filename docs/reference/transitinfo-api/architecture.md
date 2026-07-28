# TransitInfoAPI — Architecture and Why It Exists

## The problem

Public transit data is published as **GTFS** — a zip of CSV files that each operator produces
independently. That format has one property that shapes this entire service:

> **Every operator invents its own IDs.** Croatian Railways calls Zagreb main station `HZ-1001`; ÖBB
> calls the same physical building `8100108`; ZET's tram stop outside it is `216`. Nothing in GTFS
> says these are related.

So a naive importer produces one row per operator per stop, and a user searching for "Zagreb" gets
four results that are all the same place. Making that usable is the hard problem, and the answer is
the **raw stop → canonical station** model documented in [reconciliation.md](reconciliation.md).

The rest follows: you need somewhere to put the feeds (`Feed`, `FeedVersion`), a way to re-import
them when operators publish updates (`FeedPollingWorker`), live positions on top of the static
schedule (`RealtimeManager`), and an admin console for a human to adjudicate the matches the
algorithm is not sure about.

## Why it is a separate service

See [../getthere-api/transit-integration.md](../getthere-api/transit-integration.md#why-there-are-two-services-at-all)
for the full argument. Briefly: this service does **bulk imports that rewrite entire tables**, holds
only public data, and changes when operators publish — none of which should share a database or a
release cycle with a wallet ledger.

### The independence rule

**TransitInfoAPI references no other project in this solution** — not `GetThereShared`, not
`GetThereAPI`, not the client. This is a hard architectural boundary, and the `.csproj` carries a
comment saying so where the reference would otherwise go.

Each service therefore has its own `PermissionKeys`, `AppException`, `RoleNames`, `AuditLog`,
`RefreshToken` and `GeoConstants`. That duplication is the intended cost. The services have separate
user stores, separate permission vocabularies (feeds and stations here; wallets and tickets there) and
separate roles (`Admin`/`Client` here, `Admin`/`User` there) — so types that look alike today are
alike by coincidence, not by contract. Sharing them would couple two independent release cycles and
let a change made for one service silently alter the other.

The only channel between them is HTTP: GetThereAPI authenticates as an ordinary user of this service
and maps the responses into its own types. Nothing here knows GetThereAPI exists.

> A `ProjectReference` to `GetThereShared` did exist, used only for `RoleDto`/`UserDto` in
> `RoleController`/`RolePermissionManager`. It has been removed and those types now live in
> `TransitInfoAPI.Contracts/RoleContract.cs`. Treat its reappearance as a regression.

---

## Layering

```
Controllers/   HTTP shape only
Managers/      Business logic and database access
Services/      GtfsParser, ExternalFeedSource, ImportLogStore, DynamicClaimsTransformation
Workers/       Three BackgroundServices that keep data fresh
Core/          IFeedSource abstraction, GBFS models
Entities/      EF Core model
Contracts/     Response DTOs — this service's own, not shared
Mapping/       Entity → contract projections
Common/        PermissionKeys, GeoJsonGeometry, Paginated, RoleNames
feeds/         On-disk GTFS archives, one directory per feed
```

Unlike GetThereAPI, managers here are registered **explicitly** rather than by namespace convention,
because their lifetimes genuinely differ:

| Lifetime | Services | Why |
|---|---|---|
| **Scoped** | `FeedManager`, `ReconciliationManager`, `StationManager`, `RouteManager`, `OperatorManager`, `ScheduleManager`, `PlaceMatchingManager`, `MobilityManager`, `CountryManager`, `GtfsParser`, auth managers | Hold a `DbContext` |
| **Singleton** | `RealtimeManager`, `OnestopIdManager`, `ImportLogStore`, `ExternalFeedSource` | Hold process-wide state or are pure |

`RealtimeManager` being a **singleton is load-bearing** — it holds the in-memory vehicle and trip-update
caches, which are the live data. It takes an `IServiceScopeFactory` rather than a `DbContext`, because
a singleton cannot capture a scoped dependency. Getting this wrong produces a captive-dependency bug
where one `DbContext` lives for the process lifetime.

`OnestopIdManager` is a singleton because it is **pure** — deterministic ID generation with no state.
That matters: the same stop must produce the same ID on every import, forever.

---

## The database model in one picture

```
Operator ──┬── Feed ──── FeedVersion ──┬── Agency
           │                           ├── RawStop ──────┐
           │                           ├── Trip ── StopTime
           │                           ├── Calendar / CalendarDate
           │                           └── Shape          │
           │                                              │ reconciliation
           ├── CanonicalRoute                             ▼
           └── CanonicalStationOperator ─── CanonicalStation ─── Place
                                                  │
                                            Country ── City
```

The important split is the middle column:

- **`RawStop`** — what one feed version literally said. Immutable history, scoped to a `FeedVersion`.
- **`CanonicalStation`** — the real-world place. Stable across feeds, operators and re-imports.
  This is what has an `OnestopId`, and what clients actually query.

Everything in [reconciliation.md](reconciliation.md) is about the arrow between them.

### Two global conventions in `TransitDbContext`

**Every enum is stored as a string**, applied by a loop over the whole model, with
`SetMaxLength(50)`. Readable in the database and — critically — feed data outlives code, so a
reordered enum must not silently reinterpret existing rows. `RouteType` in particular uses GTFS's own
sparse numbering (`Trolleybus = 11`, `Bicycle = 100`, `Airplane = 200`), which string storage keeps
honest.

**Every foreign key is `DeleteBehavior.Restrict`**, applied by the same loop. The stated reason is
that SQL Server rejects multiple cascade paths, and this graph has many — `FeedVersion` reaches
`CanonicalStation` through both `RawStop` and `StopTime`. The consequence is that deletion is always
explicit and ordered, which is why `FeedManager.DeleteAsync` walks the child tables by hand in
dependency order.

`StationMergeLog` goes further with `NoAction` on both its station FKs, since it deliberately
references a station that may since have been deactivated — the log has to outlive the merge.

### `StopTime` and the 450-character limit

```csharp
// 450 chars × 2 bytes (NVARCHAR) = 900 bytes = SQL Server non-clustered index key limit
[MaxLength(450)] public string RawStopId { get; set; }
```

`StopTime` is the largest table in the system — millions of rows for a national feed — and it carries
both `RawStopId` (the feed's own string) and `RawStopEntityId`/`CanonicalStationId` (resolved FKs).
The string is kept because the FK is backfilled *after* the bulk insert; see
[feed-pipeline.md](feed-pipeline.md#the-backfill-step).

The covering index is the one that makes departures fast:

```csharp
.HasIndex(st => new { st.CanonicalStationId, st.DepartureTime })
.IncludeProperties(st => st.TripId);
```

---

## Authentication and authorization

The model is the same claims-based one as GetThereAPI — permission strings, one policy per key, Admin
short-circuits everything, `DynamicClaimsTransformation` reloads claims per request. See
[../getthere-api/architecture.md](../getthere-api/architecture.md#the-authorization-model-and-why-it-is-claims-based-rather-than-role-based).

Three things differ, and all three matter.

### 1. There is no public registration

`POST /auth/register` requires `users.manage`. This service has no end users — only admins and
service accounts. Accounts are created *by* an admin, never self-served.

### 2. Two roles, and the Client role is derived

| Role | Permissions |
|---|---|
| `Admin` | Everything in `PermissionKeys.All` |
| `Client` | Every permission **ending in `.view`**, computed at startup |

```csharp
foreach (var perm in PermissionKeys.All.Where(p => p.EndsWith(".view", StringComparison.Ordinal) && …))
    await roleManager.AddClaimAsync(clientRole!, new Claim("permission", perm));
```

`Client` is what GetThereAPI's service account holds. Deriving it from the `.view` suffix means a new
read permission is automatically granted to clients — convenient, but it also means **the naming
convention is a security boundary**. A permission named `stations.view` that in fact mutates
something would be handed to every client at the next restart.

### 3. Some read endpoints are anonymous

These carry `[AllowAnonymous]` despite the class-level `[Authorize]`:

| Endpoint | |
|---|---|
| `GET /stations` | List with geo filter |
| `GET /stations/{id}` | |
| `GET /stations/{id}/departures` | |
| `GET /routes` | |
| `GET /routes/{id}` | |
| `GET /mobility/stations` | |
| `GET /health` | |

This exists so the bundled map console can render without a login. It is defensible — the data is
public reference data, published by operators under open licences — but note the asymmetry: `GET
/stations/{id}/operators` and `/stations/search` **do** require `stations.view` while `/stations`
itself does not. The anonymous surface is only protected by the global 100/min IP rate limit, so it is
the part of this service most exposed to scraping.

---

## Error model and the seeding differences

Errors are `ProblemDetails` produced from `AppException`, same shape as GetThereAPI, with one
difference worth knowing: this service sets `Detail` to the exception message for `AppException`s and
to a fixed `"An unexpected error occurred."` otherwise. **There is no development-mode leak of the
real exception message here**, unlike GetThereAPI.

Startup seeding mirrors GetThereAPI's — refuse to create an admin outside Development without
`Seed:AdminPassword`, write a generated one to `.admin-credentials` in Development only. It adds a
second account:

```
admin@transit.local      Admin   ← human operator
getthere-api             Client  ← GetThereAPI's service account
```

**`Seed:ServiceAccountPassword` must equal GetThereAPI's `TransitInfoApi:ClientSecret`.** They are two
halves of one credential configured in two places, and nothing validates that they match — a mismatch
shows up as GetThereAPI returning 502 on every map endpoint. This is the single most common
integration failure between the two services.

---

## Startup: crash recovery

`Program.cs` runs `db.Database.MigrateAsync()` on every start — **migrations are applied
automatically**, unlike GetThereAPI where they are not.

Then it does something unusual and important:

```csharp
var stuck = await db.FeedVersions.Where(fv => fv.ImportStatus == FeedImportStatus.Importing)…
```

Any `FeedVersion` still marked `Importing` means the process died mid-import. Because a GTFS import is
a multi-table bulk insert that is **not** wrapped in one transaction, that leaves partial data. So
startup marks those versions `Failed` and deletes their partial rows in dependency order:

```
StopTimes → RawStops → Trips → Calendars → CalendarDates → Shapes
```

Without this, a crashed import leaves half a feed in the database, and the next import merges into
garbage. It is the recovery half of the "no single transaction" trade-off explained in
[feed-pipeline.md](feed-pipeline.md#why-there-is-no-single-transaction).

---

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | — | **Required.** 120 s command timeout, NetTopologySuite enabled |
| `Jwt:Key` | — | **Required**, ≥32 bytes, not `CHANGE-ME` |
| `Jwt:Issuer` / `Audience` | — | Validated on every token |
| `Seed:AdminPassword` | — | Required outside Development |
| `Seed:ServiceAccountPassword` | — | **Must match GetThereAPI's `ClientSecret`** |
| `FeedPolling:IntervalMinutes` | 60 | Static feed check interval |
| `FeedPolling:MaxConsecutiveFailuresBeforeDeactivate` | 10 | Auto-disable a broken feed |
| `FeedImport:BulkCommandTimeoutSeconds` | 600 | Bulk statements far exceed the 30 s default |
| `RealtimePolling:*` | | Interval, vehicle staleness cutoff, failure threshold |
| `MobilityPolling:*` | | GBFS interval |
| `PlaceMatching:MaxDistanceMeters` | 50000 | Station → place radius |
| `PlaceMatching:DefaultCountryIsoCode` | `HR` | Fallback when geo-detection fails |
| `Reconciliation:AutoMergeNameThreshold` | 0.90 | See [reconciliation.md](reconciliation.md) |
| `Reconciliation:AutoMergeDistanceMeters` | 100 | |
| `Reconciliation:ManualReviewNameThreshold` | 0.70 | |
| `Reconciliation:ManualReviewDistanceMeters` | 300 | |
| `Schedule:Timezone` | `Europe/Zagreb` | GTFS times are local; this converts them |
| `Feeds:AllowPrivateNetworkUrls` | `false` | **Never enable in production** — see below |

Two defaults encode that this is a Croatia-first deployment: `DefaultCountryIsoCode = "HR"` and
`Schedule:Timezone = "Europe/Zagreb"`. Both are correct for the current feeds and both are wrong the
moment a feed outside that timezone is added — GTFS departure times are local to the *agency*, and
this service applies one configured zone to all of them.

### SSRF protection on feed URLs

Feed URLs are admin-supplied and fetched by the server, which is textbook SSRF. `ExternalFeedSource`
resolves the host and **rejects private, loopback and link-local addresses** — and re-checks after
redirects, because a redirect can land somewhere the original check never saw.

`Feeds:AllowPrivateNetworkUrls` disables that, as an escape hatch for developing against a locally
hosted GTFS zip. The code logs a warning at startup when it is on, and the comment is blunt: it turns
the feed importer back into an SSRF proxy for the server's own network.

---

## CORS: deliberately absent

```csharp
// CORS is intentionally not configured — all browser consumers (admin UI, map)
// are served from the same origin. Server-to-server callers don't need CORS.
```

GetThereAPI is a server-to-server caller, so it is unaffected. Note the CSP here is **looser** than
GetThereAPI's — it allows `cdn.jsdelivr.net` and `unpkg.com` for the Bootstrap and MapLibre the legacy
admin pages still load, plus `img-src https:` for map tiles.

---

## Related documents

- [feed-pipeline.md](feed-pipeline.md) — GTFS import, versioning, polling
- [reconciliation.md](reconciliation.md) — the station identity problem
- [realtime.md](realtime.md) — GTFS-RT, GBFS, and the in-memory caches
- [endpoints.md](endpoints.md) — the HTTP surface
