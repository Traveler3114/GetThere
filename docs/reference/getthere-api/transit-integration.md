# GetThereAPI ↔ TransitInfoAPI — The Integration

> **SUPERSEDED, 2026-08-02. The integration described below no longer exists.** GetThereAPI makes no
> HTTP call to TransitInfoAPI and holds no credentials for it. `TransitInfoApiClient`, `MapManager`,
> `MapProxyController`, the `TransitInfoApi` configuration block and the `map.view` permission are
> deleted; the service account `getthere-api@transit.local` has no caller.
>
> This finished what the map-proxy migration started. That change moved the map page to
> TransitInfoAPI and left exactly one endpoint behind — a reachability probe for the admin console's
> status dot. The probe authenticated with the service account to prove the service account worked,
> which is circular once nothing else uses it, so the dot and the credential went together.
>
> **What survives is the domain link, and it is not an integration.**
> `TicketingAdapter.TransitInfoGlobalId` is still a column, still indexed, still rendered in the
> adapters admin page. It is a **string, not a foreign key, and not a call** — a soft reference to an
> operator's Onestop ID upstream, resolved by a human rather than by this service.
>
> Everything below is kept deliberately. It is the record of why the coupling was built the way it
> was, and it is the argument any future integration has to answer — particularly the allowlist
> section and the 502-not-500 rule. Read it as history, not as a description of running code.

## Why there are two services at all

The two systems solve genuinely different problems and change at completely different rates:

| | GetThereAPI | TransitInfoAPI |
|---|---|---|
| Owns | Users, wallets, tickets, journeys | Stations, routes, schedules, live vehicles |
| Data source | User actions | GTFS feeds, GTFS-RT, GBFS |
| Write pattern | Small transactional writes | Bulk imports of hundreds of thousands of rows |
| Data scope | One user's data | Public, shared by everyone |
| Sensitivity | Personal and financial | Public reference data |

Merging them would put a bulk feed import — which rewrites entire tables — in the same database as
the wallet ledger. It would also mean every transit-data schema change risks the money path.

They are also **fully independent at the assembly level, and this is a hard rule**: TransitInfoAPI
takes no `ProjectReference` to `GetThereShared`, `GetThereAPI` or the client. Each service carries its
own `GeoConstants`, `PermissionKeys`, `AppException`, `RoleNames`, `AuditLog` and `RefreshToken`. The
duplication is intentional — a shared library would couple two independent release cycles and let a
change made for one service silently alter the other.

The dependency ran **one way, over HTTP only**. GetThereAPI authenticated as an ordinary user of
TransitInfoAPI and mapped its responses into its own types; TransitInfoAPI knew nothing about
GetThereAPI at all. There is now no dependency in either direction — the assembly-level independence
below is still a hard rule, and the HTTP link that used to sit beside it is gone too.

> A `ProjectReference` to `GetThereShared` did exist here, used only for `RoleDto`/`UserDto`. It was
> removed and those types now live in `TransitInfoAPI.Contracts`. Treat its reappearance as a
> regression.

The one thing that links the domains is `TicketingAdapter.TransitInfoGlobalId` — a soft reference from
a GetThereAPI adapter row to an operator's Onestop ID upstream. It is a **string, not a foreign key**,
precisely because the two databases must be able to move independently.

---

## The one-way rule

```
MAUI client  ──►  GetThereAPI          (business data)
MAUI client  ──►  TransitInfoAPI       (the map, anonymous, same-origin with the page)
```

For business data, the client talks only to GetThereAPI, and everything in this document existed to
make that enforceable rather than conventional. See
[architecture.md](architecture.md#what-this-service-is-for) for why.

> **The second hop is gone entirely.** The arrow that used to run
> `GetThereAPI ──► TransitInfoAPI` has no code behind it. The client loads the map page from
> TransitInfoAPI and reads that service's anonymous endpoints same-origin; GetThereAPI is not
> involved in the map at any point and no longer authenticates upstream for anything. See
> [`map-proxy-migration.md`](../../map-proxy-migration.md).

---

## `TransitInfoApiClient`

An `HttpClient`-typed service, registered with a 30-second timeout and a base address from
`TransitInfoApi:BaseUrl`.

### Authentication: a service account, not a shared secret

GetThereAPI logs into TransitInfoAPI as an ordinary user — `TransitInfoApi:ClientId` and
`ClientSecret` are an email and password — and gets a JWT back. It is a normal account with normal
permissions, which is what allows TransitInfoAPI to authorize it per-endpoint like any other caller.

The token is cached in **`static` fields**, so it is shared across every request in the process rather
than per-client-instance. That is the right lifetime — one process needs one upstream session — but it
does mean the cache is not per-tenant and would need rethinking if this service ever served multiple
upstream identities.

Concurrency is handled with a `SemaphoreSlim(1,1)` and **double-checked locking**: check the token,
take the lock, check again. Without the second check, every request queued behind the lock during a
refresh would perform its own login.

### Token expiry, and why the fallbacks are pessimistic

`GetTokenExpiry` decodes the JWT payload (base64url, padded via `Base64Helper.PadBase64`) and reads
the `exp` claim, then **subtracts 5 minutes** as a safety margin. Requests also treat a token as stale
when it has under 5 minutes left.

Two fallbacks, both deliberately short:

| Situation | Assumed lifetime |
|---|---|
| No readable `exp` claim | 10 minutes, with a warning |
| Payload unparseable | 10 minutes, with a warning |
| Token has fewer than 2 segments | 1 hour |

The reasoning is stated in the code: better to re-authenticate too often than to treat an unknown
token as long-lived. Falling back silently would cache a 15-minute token for an hour and send expired
credentials upstream until the 401 retry kicked in — which is why the failure is *logged* rather than
swallowed.

### The 401 retry

Every request retries **once** on 401: invalidate the cached token, re-authenticate, resend. This
covers the race where a token expires between the staleness check and the request arriving upstream.

Only one retry, and only on 401. A persistent 401 means the credentials are wrong, and retrying that
in a loop would lock the service account out.

### Everything upstream becomes a 502

Any upstream failure — unreachable, timed out, non-success status, unreadable body — throws
`AppException(502, "TRANSIT_UPSTREAM_UNAVAILABLE")`.

This is the point of the wrapper. Letting `HttpRequestException` bubble to the global handler would
produce a bare 500, telling the client that GetThereAPI is broken when in fact a dependency is down.
The client shows a different message for each, and 502 is the honest one.

The catch is narrowed to `HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested`
— so a genuine caller cancellation still propagates as cancellation rather than being misreported as
an upstream outage.

### Pagination — a bug worth not reintroducing

`FetchAllPagesAsync` walks every page of an upstream list, 500 per page, up to a **10,000-item
ceiling**.

The original code requested a single page of 500 and returned it, silently dropping everything beyond
— a city with more than 500 stations simply lost the rest, with no error and no log line. Paging to
exhaustion is the fix.

The ceiling stops a pathological feed pulling an entire national dataset into memory, and **is logged
when it bites**, so the truncation is visible rather than silent. That log line is the signal to either
raise the cap or narrow the query.

The loop stops on: an empty page, reaching the reported total, a short page, or the cap.

### Two bugs that came from formatting and geometry

**Invariant culture on coordinates.** `BuildGeoQuery` uses `FormattableString.Invariant`. Interpolating
a `double` under a comma-decimal culture — `hr-HR`, entirely plausible for this deployment — emits
`lat=45,8`, which the upstream API cannot parse. The project is Croatian; this is not hypothetical.

**Centred bounding boxes.** `GetVehiclesAsync` converts a radius to a lat/lon box using
`GeoConstants.KmPerDegree = 111.0`. It previously *added* the offset to both edges, producing a box
extending only north-east — vehicles south or west of the caller were never returned. It now subtracts
for the minimum and adds for the maximum.

### The `PaginatedResponse<T>` property-name bug

Worth recording because of how long it hid. This class mirrors TransitInfoAPI's `Paginated<T>`, whose
list property is `data`. It originally declared `Items`, which matches nothing upstream sends — so it
deserialised to an empty list **every time**, and the typed map endpoints that once sat on top of it
(`/api/map/stations`, `/routes`, `/mobility/stations`) silently returned `[]` for their entire
existence.

Nothing noticed because the map page was calling TransitInfoAPI directly at the time — as, by a route
nobody planned, it does again. The lesson still stands for every other read this client makes: these
hand-maintained mirror DTOs have no compile-time link to the upstream contract — the two projects
share no assembly — so a rename upstream fails silently here. That is the price of the
deliberate independence, and it is why `JsonSerializerOptions.PropertyNameCaseInsensitive = true` is
set but nothing stronger can be.

---

## `MapManager` — caching and the allowlist

### Two cache tiers, for a reason (removed with the proxy)

> **Gone.** These tiers lived in `MapManager` and served the map proxy; the map reads TransitInfoAPI
> directly now, so there is nothing here to cache and `MapManager` no longer takes an `IMemoryCache`
> at all. Recorded because the pressure it answered did not go away — it moved upstream, where a
> panning client now reaches TransitInfoAPI's database on every viewport change. If that shows up as
> load, response caching on `/stations` and `/routes` in TransitInfoAPI is the equivalent fix, and
> the reasoning below is the shape of it.

Panning a map re-requests the same tiles constantly, and every one of those was previously a fresh
round trip upstream. But not all map data ages the same way:

| Tier | TTL | Applies to | Why |
|---|---|---|---|
| Reference data | **2 minutes** | Stations, routes | Change on feed import, not by the second |
| Live data | **5 seconds** | Vehicles, mobility docks | Just enough to collapse the burst a pan produces, without showing a stale vehicle position |

Cache keys are built with `Invariant($"map:stations:{lat}:{lon}:{radiusKm}")` — invariant formatting
again, so the same viewport does not produce two different keys under two cultures.

**Failures are never cached.** `CachedAsync` only stores after `load()` returns, so an exception
propagates without being memoised — a 502 must not stick for two minutes.

Every entry sets `Size = 1`, required because the shared `IMemoryCache` has `SizeLimit = 2_000`. The
limit exists because viewport-keyed entries are unbounded in principle; see
[architecture.md](architecture.md#the-shared-imemorycache-and-its-size-limit).

Note that `/departures`, `/operators/station/{id}` and `/transport-types` go through `GetRawAsync`
**uncached** — departures are time-sensitive enough that caching them would be wrong.

### The upstream allowlist (removed — kept for the reasoning)

> **This passthrough no longer exists.** It was deleted with the rest of the map proxy once the map
> page moved to TransitInfoAPI, because there is no longer a page here that needs upstream shapes.
> The reasoning below is retained deliberately: it is the argument any future passthrough has to
> answer, and the allowlist is the pattern to copy if one is ever added back.

`/api/map/upstream/{**path}` forwarded a request to TransitInfoAPI verbatim. That is dangerous by
default: without a guard it is an open gateway to TransitInfoAPI **carrying the service account's
credentials**, letting any user holding `map.view` — which is every user — reach feed imports, station
merges, and reconciliation upstream.

The guard was an **allowlist of anchored regexes**, not a blocklist:

```
^stations$
^routes$
^mobility/stations$
^realtime/vehicles$
^realtime/alerts$
^stations/\d+/departures$
^stations/\d+/operators$
^map/transport-types$
```

Anchoring on both ends is what makes this safe — an unanchored pattern would match
`admin/stations/danger`. The `\d+` segments mean only numeric station ids pass, so no path component
can be smuggled through the id position.

A non-matching path returns **404 `UNKNOWN_MAP_RESOURCE`** and logs a warning. 404 rather than 403 is
deliberate: the endpoint does not confirm which upstream paths exist.

The query string is forwarded untouched, which is safe because the upstream endpoints reached are all
reads and validate their own parameters.

**Adding an entry to this list is a security decision**, not a routine change. The path becomes
reachable by every authenticated user.

### Why a passthrough existed at all

The typed endpoints re-mapped upstream shapes into `GetThereShared` contracts. The map page consumed
upstream shapes **directly** — GeoJSON feature collections in particular — and re-modelling one into
a C# type only to serialise it back to identical JSON adds drift with no benefit.

So the passthrough was what let the client honour the one-way rule while still rendering those
shapes. That framing is worth keeping, because it is also the argument that ended it: once the page
is served by the service that owns the GeoJSON, there is no origin to bridge and nothing to re-ship.
The whole apparatus was a consequence of *where the page was hosted*, not of what it needed.

---

## Failure modes

| Failure | What the client sees |
|---|---|
| TransitInfoAPI down | 502 `TRANSIT_UPSTREAM_UNAVAILABLE` |
| Service credentials wrong/expired | 502 `TRANSIT_UPSTREAM_UNAVAILABLE` (logged as a credential rejection) |
| Path not in the allowlist | 404 `UNKNOWN_MAP_RESOURCE` |
| Query too broad (>10k items) | Truncated silently to the client, **logged** server-side |
| Upstream renames a JSON property | Field silently null/empty — no compile-time protection |

The last two are the ones to watch, because neither surfaces as an error to anyone looking at the app.
Both are visible only in server logs.

---

## Operational notes

**All obsolete — recorded for anyone reading the history.** GetThereAPI needs no configuration for
TransitInfoAPI, no service account, and no network path to it. The two can be started, stopped,
deployed and version-bumped independently, and neither has an opinion about whether the other is
running.

- The **service account had to exist in TransitInfoAPI** with permission to read stations, routes,
  realtime and mobility. Creating it was a TransitInfoAPI-side task; nothing in GetThereAPI ever
  provisioned it. It still exists upstream and is now unused — see
  [`docs/secrets-rotation.md`](../../secrets-rotation.md) if you want it removed rather than left
  dormant.
- The default `BaseUrl` (`https://localhost:5001`) matched TransitInfoAPI's Kestrel HTTPS endpoint, so
  running both locally needed no configuration.
- Map endpoints were the only part of GetThereAPI that depended on TransitInfoAPI, and wallets,
  tickets, imports and journeys already worked with it completely down. Removing them is why that
  caveat no longer needs stating: **nothing** in GetThereAPI depends on TransitInfoAPI.
