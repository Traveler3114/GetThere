# TransitInfoAPI — Endpoint Reference

Every controller carries a class-level `[Authorize]`, with per-action permission policies on top.
**The Admin role satisfies every policy**, so the permission column describes what a non-admin needs.
The `Client` role — held by GetThereAPI's service account — automatically holds every permission
ending in `.view`.

Errors are `ProblemDetails` with `Title` set to the error code and `Detail` to the message.

List endpoints return `Paginated<T>`:

```json
{ "data": [ … ], "total": 1234, "page": 1, "perPage": 50, "totalPages": 25 }
```

> The property is **`data`**, not `items`. GetThereAPI's mirror DTO originally declared `Items` and
> silently deserialised to an empty list for its entire existence — see
> [../getthere-api/transit-integration.md](../getthere-api/transit-integration.md#the-paginatedresponset-property-name-bug).

Enums serialise as **strings** here (`JsonStringEnumConverter` is registered globally), unlike
GetThereAPI where they are integers. Clients consuming both must handle each accordingly.

---

## Anonymous endpoints

These carry `[AllowAnonymous]` so the bundled map console renders without a login:

| Endpoint |
|---|
| `GET /stations` |
| `GET /stations/{id}` |
| `GET /stations/{id}/departures` |
| `GET /routes` |
| `GET /routes/{id}` |
| `GET /mobility/stations` |
| `GET /health` |

Note the asymmetry: `/stations` is public but `/stations/search` and `/stations/{id}/operators`
require `stations.view`. Anonymous access is protected only by the global 100/min per-IP limit, making
this the surface most exposed to scraping.

---

## `/auth` — AuthController

Rate-limited to 10/min per IP.

| Method | Route | Auth |
|---|---|---|
| POST | `/auth/login` | Anonymous |
| POST | `/auth/refresh` | Anonymous |
| POST | `/auth/logout` | Authenticated |
| POST | `/auth/change-password` | Authenticated |
| POST | `/auth/register` | **`users.manage`** |

**Registration is admin-only.** This service has no end users — only admins and service accounts — so
accounts are created by an admin, never self-served. This is the main structural difference from
GetThereAPI's auth surface.

Token mechanics are otherwise identical: JWT access tokens, hashed rotating refresh tokens with reuse
detection and IP binding. See
[../getthere-api/architecture.md](../getthere-api/architecture.md#authentication-why-refresh-tokens-are-shaped-the-way-they-are).

`POST /auth/login` is the endpoint GetThereAPI's `TransitInfoApiClient` calls with the
`getthere-api` service account.

---

## `/stations` — StationsController

| Method | Route | Permission |
|---|---|---|
| GET | `/stations` | **Anonymous** |
| GET | `/stations/search` | `stations.view` |
| GET | `/stations/{id}` | **Anonymous** |
| GET | `/stations/by-onestop/{onestopId}` | `stations.view` |
| GET | `/stations/{id}/operators` | `stations.view` |
| GET | `/stations/{id}/routes` | `stations.view` |
| GET | `/stations/{id}/departures` | **Anonymous** |
| POST | `/stations/{id}/rematch-place` | `stations.manage` |
| GET | `/stations/{id}/reconciliation-detail` | `stations.manage` |

`GET /stations` supports geographic filtering (`lat`, `lon`, `radiusKm`) plus pagination. This is what
GetThereAPI's `/api/map/stations` proxies, paging to exhaustion at 500 per page.

`GET /stations/{id}/departures` is the schedule query described in
[realtime.md](realtime.md#departures-schedulemanager) — GTFS times resolved through the configured
timezone, valid services computed for today and tomorrow, realtime delays overlaid.

**`/by-onestop/{onestopId}` is the stable lookup.** Numeric `Id` is a database identity that a
rebuild would not preserve; `OnestopId` is derived deterministically from the station's own
properties. Any external system storing a reference — GetThereAPI stores one on every imported
ticket — should store the OnestopId and resolve through this route.

`POST /{id}/rematch-place` re-runs place and country matching for one station, for use after
correcting coordinates or adding place data.

---

## `/routes` — RoutesController

| Method | Route | Permission |
|---|---|---|
| GET | `/routes` | **Anonymous** |
| GET | `/routes/{id}` | **Anonymous** |
| GET | `/routes/by-onestop/{onestopId}` | `routes.view` |
| GET | `/routes/{id}/shape` | `routes.view` |
| PUT | `/routes/{id}/shape` | `routes.manage` |
| GET | `/routes/{id}/stops` | `routes.view` |
| GET | `/routes/{id}/trips` | `routes.view` |

Shapes are **GeoJSON LineStrings**, stored as NetTopologySuite geometry.

`PUT /{id}/shape` sets `CanonicalRoute.ShapeEdited = true`, which is what
`CarryForwardManualEditsAsync` reads during the next import to preserve the hand-drawn geometry
instead of overwriting it with the feed's. Without that flag, every re-import would discard manual
corrections — the reason the flag exists at all.

---

## `/operators` — OperatorsController

| Method | Route | Permission |
|---|---|---|
| GET | `/operators` | `operators.view` |
| GET | `/operators/{id:int}` | `operators.view` |
| GET | `/operators/by-onestop/{onestopId}` | `operators.view` |
| GET | `/operators/{globalId}` | `operators.view` |
| GET | `/operators/types` | `operators.view` |
| GET | `/operators/{id:int}/service-area` | `operators.view` |
| GET | `/operators/{globalId}/stations` | `operators.view` |
| GET | `/operators/{globalId}/routes` | `operators.view` |
| GET | `/operators/{globalId}/feeds` | `operators.view` |
| POST | `/operators` | `operators.manage` |
| PUT | `/operators/{globalId}` | `operators.manage` |
| DELETE | `/operators/{globalId}` | `operators.manage` |

Note the route ordering hazard: `{id:int}` is constrained to integers so it does not swallow
`{globalId}` or the literal `types`. Adding another `/operators/{something}` route without a
constraint will shadow one of these.

**`GlobalId` is the identifier GetThereAPI stores** in `TicketingAdapter.TransitInfoGlobalId` — the
join between the ticketing system and transit data, and what backs
`MapOperatorResponse.HasTicketing`.

`/service-area` returns the convex hull of the operator's stations as GeoJSON, computed from feed
version hulls.

---

## `/feeds` and `/feed-versions`

| Method | Route | Permission |
|---|---|---|
| GET | `/feeds` | `feeds.view` |
| POST | `/feeds` | `feeds.manage` |
| PUT | `/feeds/{id}` | `feeds.manage` |
| DELETE | `/feeds/{id}` | `feeds.manage` |
| POST | `/feeds/{id}/fetch` | `feeds.manage` |
| GET | `/feeds/{id}/versions` | `feedversions.view` |
| GET | `/feeds/versions/{versionId}/logs` | `feedversions.view` |
| GET | `/feed-versions` | `feedversions.view` |
| GET | `/feed-versions/{sha1}` | `feedversions.view` |
| GET | `/feed-versions/{sha1}/stops` | `feedversions.view` |

`GET /feeds` hides internal feeds unless `showInternal=true`.

**`POST /feeds/{id}/fetch` triggers a fetch, not necessarily an import.** If the SHA-1 matches an
existing version, nothing happens — see
[feed-pipeline.md](feed-pipeline.md#sha-1-of-the-full-content-is-the-only-unchanged-signal). It is
serialised against the polling worker by the per-feed semaphore.

`GET /feeds/versions/{versionId}/logs` returns the in-memory `ImportLogStore` lines for watching an
import progress. **These do not survive a restart**; the durable record is `ImportStatus` and
`ImportError` on the version.

Feed versions are addressed by **`sha1`, not by id** — content-addressed, so the identifier is stable
and meaningful.

`DELETE /feeds/{id}` is a heavy, ordered cascade that deactivates rather than deletes canonical
stations; see [feed-pipeline.md](feed-pipeline.md#deleting-a-feed).

Validation on create/update: absolute HTTP(S) URL only, refresh interval ≥ 60 seconds, valid
`FeedType`. A static feed URL not ending in `.zip` logs a warning but is allowed. URLs are additionally
SSRF-checked at fetch time by `ExternalFeedSource`.

---

## `/realtime` — RealtimeController

| Method | Route | Permission |
|---|---|---|
| GET | `/realtime/vehicles` | `realtime.view` |
| GET | `/realtime/tripupdates` | `realtime.view` |
| GET | `/realtime/alerts` | `realtime.view` |

Vehicles and trip updates are served **from memory** and never touch the database, which is why
`/tripupdates` is not even `async`. Alerts are read from the `Alerts` table.

`/vehicles` filters by `feedId` and a bounding box (`minLat`, `maxLat`, `minLon`, `maxLon`).
GetThereAPI converts a radius into that box — and got it wrong once, producing a box extending only
north-east; see
[../getthere-api/transit-integration.md](../getthere-api/transit-integration.md#two-bugs-that-came-from-formatting-and-geometry).

`/alerts` filters by `stopOnestopId` and `routeOnestopId`. Those match against delimited string
columns rather than relations, so filtering is a `LIKE` — see
[realtime.md](realtime.md#alerts).

Data is at most `RealtimePolling:IntervalSeconds` (30) old, and vehicles unheard from for
`VehicleStaleCutoffMinutes` (5) disappear.

---

## `/mobility` — MobilityController

| Method | Route | Permission |
|---|---|---|
| GET | `/mobility/stations` | **Anonymous** |
| GET | `/mobility/countries` | `mobility.view` |

Docked bike and scooter share from GBFS, supporting the same `lat`/`lon`/`radiusKm` filter as transit
stations. `/countries` lists which countries have mobility data, for populating a filter.

---

## `/reconciliation` — ReconciliationController

The admin surface for the station identity problem. See [reconciliation.md](reconciliation.md).

| Method | Route | Permission |
|---|---|---|
| GET | `/reconciliation/pending` | `reconciliation.view` |
| GET | `/reconciliation/auto-merged` | `reconciliation.view` |
| GET | `/reconciliation/by-station/{stationId}` | `reconciliation.view` |
| GET | `/reconciliation/{id}` | `reconciliation.view` |
| GET | `/reconciliation/split-log?candidateStationId=` | `reconciliation.view` |
| GET | `/reconciliation/merge-log` | `reconciliation.view` |
| GET | `/reconciliation/merge-preview?stationAId=&stationBId=` | `reconciliation.view` |
| GET | `/reconciliation/check-action-warning?stationAId=&stationBId=` | `reconciliation.view` |
| POST | `/reconciliation/{id}/approve` | `reconciliation.manage` |
| POST | `/reconciliation/{id}/reject?createNewStation=` | `reconciliation.manage` |
| POST | `/reconciliation/{id}/reassign?canonicalStationId=&force=` | `reconciliation.manage` |
| POST | `/reconciliation/merge-stations?sourceStationId=&targetStationId=` | `reconciliation.manage` |
| POST | `/reconciliation/unmerge/{mergeLogId}` | `reconciliation.manage` |

The workflow this is designed around:

1. Review `/pending` — matches that scored into the review band.
2. `/approve` links the raw stop to the suggested station; `/reject` breaks the suggestion, optionally
   creating a new station instead.
3. `/reassign` points a raw stop at a *different* station than the one suggested. `force=true`
   overrides the safety warning.
4. `/merge-stations` unifies two canonical stations the importer kept apart. Check `/merge-preview`
   first — it reports `CanMerge` (both active, same `PrimaryRouteType`) and a warning string.
5. `/unmerge/{mergeLogId}` reverses a merge, restoring exactly the raw stops recorded in
   `StationMergeMovedRawStop`.

Audit both directions: `/auto-merged` shows what the algorithm did without review — the feedback loop
for tuning the thresholds — and `/split-log` shows where it declined to merge and why.

`check-action-warning` exists so the console can warn *before* an irreversible-looking action rather
than after.

---

## Reference data

| Method | Route | Permission |
|---|---|---|
| GET | `/agencies` | `agencies.view` |
| GET | `/places` | `places.view` |
| GET | `/places/{id}` | `places.view` |
| GET | `/places/{id}/operators` | `places.view` |
| GET | `/places/{id}/stations` | `places.view` |
| GET | `/countries` | `countries.view` |
| POST | `/countries` | `countries.manage` |

`Agency` is the GTFS-level entity scoped to a feed version; `Operator` is the canonical one. The same
raw-versus-canonical split as stops, and `Agency.OperatorId` is the link.

`Place` is a settlement, matched to stations within `PlaceMatching:MaxDistanceMeters`.

---

## `/admin` — RoleController

| Method | Route | Permission |
|---|---|---|
| GET | `/admin/roles` | `roles.view` |
| GET | `/admin/roles/{name}` | `roles.view` |
| POST | `/admin/roles` | `roles.manage` |
| PUT | `/admin/roles/{name}/permissions` | `roles.manage` |
| DELETE | `/admin/roles/{name}` | `roles.manage` |
| GET | `/admin/users` | `users.view` |
| PUT | `/admin/users/{userId}/role` | `users.manage` |

`RoleDto` and `UserDto` are declared in `TransitInfoAPI.Contracts/RoleContract.cs`, like every other
contract in this service. They resemble GetThereAPI's same-named types but are **not** shared: the two
services have separate user stores, separate permission vocabularies and separate roles
(`Admin`/`Client` here, `Admin`/`User` there).

Note `PUT` here versus GetThereAPI's `POST` for the equivalent user-role route: the two role admin
surfaces are similar but not identical, so a client cannot be written against one and pointed at the
other. That divergence is exactly why the types are not shared.

---

## `/health`

```
GET /health   → 200 { "status": "healthy", "timestamp": "…" }
```

Anonymous, liveness only. It does **not** check the database, feed freshness, or worker health — it
answers 200 while every feed is failing and the database is unreachable. Useful as a container
liveness probe, not as a readiness or data-quality signal.

For actual health, look at `FeedVersion.ImportStatus` per feed, `Feed.IsActive` (auto-deactivation
means repeated failure), and whether `/realtime/vehicles` returns anything.

---

## No OpenAPI document

Unlike GetThereAPI, this service does not map OpenAPI or Scalar in any environment. The contract is
`TransitInfoAPI.Contracts` plus this document; GetThereAPI's `TransitInfoApiClient` maintains
hand-written mirror DTOs against it, with no compile-time link — which is exactly how the `Items`
versus `data` bug survived.
