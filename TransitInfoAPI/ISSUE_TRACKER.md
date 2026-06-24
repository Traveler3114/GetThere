# TransitInfoAPI — Consolidated Issue Tracker + Execution Plan

> **Status Key**
> - **[FIXED]** — Resolved in prior sessions, not actionable
> - **[OPEN]** — Still broken, included in a phase below
> - **[DEFERRED]** — Blocked on policy or non-trivial redesign

---

## Phase 1 — Data Integrity (OPEN P0/P1)

| # | Issue | What | Why fix | Fix | Verify |
|---|-------|------|---------|-----|--------|
| 7 | **OperatorManager.GetTotalCountAsync missing** | Operator list total ignores countryId/q filters. Count logic lives in controller with 3 redundant queries. | Wrong pagination totals. | Add `GetTotalCountAsync(int? countryId, string? q, CancellationToken ct)` to OperatorManager. Move count logic from OperatorsController lines 84-90 into it. | GET /operators?q=x&countryId=1 returns correct total |
| 10 | **Alert dedup key too broad** | Key is (Cause, Effect, ActivePeriodStart, HeaderText). Two alerts same cause/effect/header but different routes get collapsed. | Route-specific alerts silently lost. | Widen key to include AffectedRouteIds, AffectedStopIds. | GTFS-RT with two same-cause alerts for different routes -> both persist |
| 11 | **GeoJSON stations no pagination** | format=geojson branch has .Take(5000) but no Skip/page/perPage. All results returned in one response. | Browser hang at 5000 features. | Add page/perPage with Skip/Take before the 5000 cap. | GET /stations?format=geojson&page=2&perPage=100 returns features 101-200 |
| 12 | **FeedVersions.GetStops no pagination** | Returns all raw stops for a version (up to 50K+). Has .Take(10000) cap but no pagination params. | Large responses, slow serialization. | Add page/perPage params with Skip/Take. Return Paginated<RawStopResponse>. | GET /feed-versions/{id}/stops?page=3&perPage=20 returns 20 stops from offset 40 |
| 128 | **Map vehicle fetch silently swallowed** | index.html line 194: .catch(() => {}) for vehicles fetch. No user feedback on failure. | Users see empty vehicle layer with no error. | Change to .catch(() => showMapError('Failed to load vehicles')). | Kill API while map open -> error shown for vehicles |

---

## Phase 2 — Import Pipeline (OPEN P2)

| # | Issue | What | Fix | Verify |
|---|-------|------|-----|--------|
| 18 | **Import stuck cleanup incomplete** | Startup cleanup deletes stop_times, raw_stops, trips for stuck versions but omits calendars, calendar_dates, shapes. | Add missing entity types to delete queries in FeedManager ~line 115. | Break import mid-way, restart -> all stale data removed |
| 19 | **SetCommandTimeout(600) never reset** | Import sets 10min timeout on DbContext, never resets it. Normal queries inherit 10min timeout. | Wrap in try/finally, reset to 30s. | After import, normal query doesn't hang 10min |
| 21 | **ParseStops passes (0,0) coordinates** | Empty lat/lon in CSV creates stop at null island. Not filtered. | Skip if lat==0 && lon==0, log warning with stop_id. | Feed with (0,0) stops excludes them, warning logged |
| 22 | **CsvConfig AllowComments=true** | Stop IDs starting with # silently skipped as CSV comments. | Set AllowComments = false. | Stop #001 imports correctly |
| 80 | **N+1 route import queries** | One query per route (by GlobalId, then OnestopId). 1000 routes = 1000-2000 queries. | Batch route lookup: collect all identifiers, query once with IN clause. Lines ~554-575. | 1000-route feed does ~5 queries |
| 111 | **Import modal locks on network error** | Close button disabled during import. If POST hangs, modal is non-dismissable forever. | Add AbortController timeout (60s), re-enable Close in .catch(). | Disconnect during import -> modal shows error and can close |

---

## Phase 3 — Background Workers (OPEN P2)

| # | Issue | What | Fix | Verify |
|---|-------|------|-----|--------|
| 30 | **Feed failure count not reset for null URL** | Feed with ExternalUrl=null never succeeds or fails — failure count frozen at last value. | Reset failure count in null-URL path via TryRemove. | Feed with 9 failures + URL removed -> count resets to 0 |
| 112 | **Failure counter non-atomic** | RealtimePollingWorker clears ALL feeds' failure counts when ANY single feed succeeds. | Use ConcurrentDictionary<int,int> per-feed tracking (match FeedPollingWorker pattern). | One failing feed not reset by other feeds' successes |
| 58/92 | **setInterval never cleared** | Background tab polls indefinitely. | Add pagehide listener: clearInterval for all setInterval calls. | Background tab stops polling after navigation |

---

## Phase 4 — Admin UI (OPEN P2/P3)

| # | Issue | What | Fix |
|---|-------|------|-----|
| 113 | **Countries pagination broken** | allCountries.map(c => ...) used instead of paginated list variable. Every page renders ALL countries. | Use paginated list variable |
| 114 | **Mobility table pagination broken** | Same allStations.map bug in table view. | Use paginated variable |
| 115 | **Mobility card pagination broken** | Same bug in card view. | Use paginated variable |
| 116 | **Broken DOM selector — Inspect button** | Missing closing `)` in querySelector: `expandVersions(${feedId}` | Add missing `)` |
| 117 | **Broken DOM selector — import completion** | Same missing `)` pattern after import. | Same fix |
| 129 | **Continent XSS** | c.continent rendered without esc(). | Wrap with esc() |

---

## Phase 5 — Missing Indexes (OPEN P3)

### 5.1 Add FK indexes
**File:** Data/TransitDbContext.cs

| Table | Column | Why |
|-------|--------|-----|
| CanonicalStationOperator | OperatorId | Operator→station lookups |
| CanonicalRoute | OperatorId | Route-by-operator queries |
| Feed | FeedId (string) | Lookup by business key |
| Alert | FeedId | Frequent poll query (30s) |
| Trip | CanonicalRouteId | Schedule generation |
| ReconciliationCandidate | SuggestedCanonicalStationId | Merge/unmerge queries |
| MobilityStation | MobilityProviderId | Provider→station lookups |
| City | CountryId | Country→city page loads |

### 5.2 Unique constraints
- FeedVersion.Sha1 → `.IsUnique()`
- (FeedVersionId, TripId) → `.IsUnique()`

### 5.3 Migration
```
dotnet ef migrations add AddMissingIndexesPhase5 --context TransitDbContext
dotnet ef database update
```

---

## Phase 6 — Polish & Convention (OPEN P3)

| # | Issue | What | Fix |
|---|-------|------|-----|
| 130 | StationType Unknown fallback | No Unknown=0 in enum — future GTFS location_types crash import | Add Unknown = 0 |
| 41 | PascalCase constructor params | `ReconciliationManager ReconciliationManager` in 3 controllers | camelCase |
| 43 | IsServiceActiveOnAsync dead code | Never called | Remove |
| 44 | GenerateOperatorOnestopId overload dead | Never called | Remove |
| 136 | No validation attributes on request DTOs | No [Required], [Range], [StringLength] anywhere | Add systematically |
| 133 | Exception handler leaks SQL details | InnerException.Message exposed in ProblemDetails | Remove from prod responses |
| 135 | Static locks never cleaned up | _feedLocks/_importLocks keys accumulate forever | Remove on feed delete |
| 71 | No health endpoint | No /health for monitoring | Add simple HealthController |
| 72 | ImportError bool → string | No error detail persisted on import failure | Change to nullable string + migration |
| 131 | Hardcoded 10s initial delay | RealtimePollingWorker line 38 | Move to config |
| 95 | GtfsRouteTypeMapper missing 203 | 203→Bus works by accident via default | Add explicit case |
| 32 | DataTable reuse across batches | dt.Rows.Clear() doesn't release arrays | Create new DataTable per batch |
| 102 | CsvConfig new instance per call | Fresh CsvConfiguration each call | Static readonly field |
| 137 | AlertResponse missing affected IDs | AffectedStopIds/RouteIds/etc not in response contract | Add fields |
| 140 | GeoJsonGeometry anonymous types + null! | No contract, NRE on unsupported geometry | Typed objects, throw not null |
| 97 | RouteMapper Operator null risk | Silent null if .Include missing | Add guard |
| 98 | CountryName string filter — table scan | Uses .Name instead of FK | Filter by CountryId |
| 103 | MergePreview returns anonymous type | Task<object>, no contract | Define typed response |
| 105 | Directory.CreateDirectory no error handling | Can throw on permissions | Add try/catch |
| 85 | CountriesController no pagination | Returns all countries unbounded | Add pagination |
| 84 | AutoMergeVerdict missing from list response | Only in detail endpoint, not list | Include in list response |
| 90 | VehicleResponse has no routeType | Map JS references v.routeType which is always undefined | Add routeType to contract or remove ref |
| 91 | realtime.html filter label mismatch | Placeholder says "operator" but filters by route/trip/vehicle | Fix label |
| 93 | GetReconciliationDetail placeholder CreatedAt | Nonsensical rawStop.Id heuristic | Use actual import timestamp |
| 96 | UpdateFeedRequest allows negative interval | No [Range] on RefreshIntervalSeconds | Add validation attribute |
| 99 | Bidirectional route GroupBy undefined | Known limitation — one direction's stop chosen arbitrarily | Document as-is (done) |
| 64 | ParseAgencies no empty agency_id check | Duplicate "" agency IDs | Validate or unique |
| 65 | promptReassign uses browser prompt() | Ugly, no validation | Replace with modal |
| 69 | Feed URL format validation missing | Non-zip/non-RT URL gets opaque error | Validate at create time |
| 70 | Non-zip URL returns HTML | "end of stream" error instead of "URL returned HTML" | Check Content-Type |
| 20 | BeginImportTransactionAsync no existing-tx check | UseTransaction throws if tx open | Add check |
| 23 | _feedLocks / _importLocks separate | Concurrent download + import race | Unify locks |
| 25 | ParseGtfsTimeToSeconds returns 0 for invalid | Phantom midnight departures | Return null, skip |
| 31 | GenerateFeedOnestopId always (0,0) | All feed OnestopIds geohash null island | Pass actual lat/lon |
| 33 | PickupType invalid vs null indistinguishable | Both become DBNull | Use sentinel |
| 34 | MatchStationsToPlacesAsync stale re-match | Stations re-matched every import | Widen threshold / add cooldown |
| 38 | FindNearestPlace 50km hardcoded | Rural stations permanently orphaned | Make configurable |
| 39 | LoadPlacesAsync sort has no effect | Sorted then linear scan | Remove sort |
| 40 | BackfillRouteGeometriesAsync client eval risk | Complex subquery may load all trips | Restructure or AsNoTracking |
| 42 | volatile on ConcurrentDictionary | Does nothing | Remove |
| 45 | RouteService naming | _routeService instead of _routeManager | Rename |
| 48 | EncodeGeohash off-by-boundary | 6m apart → different OnestopId | Acceptable, document |
| 52 | UpdateOperatorRequest allows empty Name | Sets name to "" | Add validation |
| 53 | CreateFeedRequest negative interval | ArgumentOutOfRangeException on worker | Validate |
| 54 | FeedManager.CreateAsync no OnestopId check | Duplicate OnestopIds possible | Add check |
| 55 | Paginated perPage=0 division by zero | Math.Ceiling(total / 0) | Validate perPage > 0 |
| 67 | SqlBulkCopy shared connection rollback risk | Same connection as EF — theoretical conflict | Monitor, labelled low risk |
| 68 | RealtimeManager loads full protobuf | ReadAsByteArrayAsync + ParseFrom doubles memory | Stream parse |
| 108 | Calendar says optional but import requires it | Validation passes then import fails | Make mandatory or handle gracefully |
| 134 | Controller defaults duplicate config | Three sources of truth | Remove controller defaults |
| 138 | No XML docs on contracts | All 13 contract files undocumented | Add XML docs / Swagger |
| 139 | HTTPS on dev server | HTTP only | Add HTTPS profile |

---

## Already Fixed (not actionable)

| # | What | Fixed in |
|---|------|----------|
| 1 | ApproveCandidateAsync missing transaction | Already has BeginTransactionAsync |
| 2 | MergeStationsAsync missing transaction | Already has BeginTransactionAsync |
| 3 | FeedId collision | This session (unique index + validation in CreateAsync) |
| 4 | SearchAsync(stationType) always empty | Base filter already conditional on stationType param |
| 5 | CanonicalRoute never deactivated | Raw SQL deactivation exists in FeedManager |
| 6 | StationManager total count ignores filters | Already passes all filters to GetTotalCountAsync |
| 8 | Realtime trip cache never evicts | Dictionary replaced wholesale each poll |
| 9 | Alert dedup N+1 queries | Single batch query, no per-alert round trips |
| 13 | StationsController.GetRoutes no limit | .Take(500) already present |
| 14 | GtfsRouteTypeMapper 1100→Funicular | Already mapped 1100→Airplane, 1200→Ferry |
| 15 | PlacesController entity-level Distinct() | Already SQL-level DISTINCT |
| 16 | FeedManager.CreateAsync throws InvalidOperationException | Already throws AppException(404) |
| 17 | Cross-midnight departures approximated | Already handles >86400s as next-day |
| 24 | Reconciliation bounding box asymmetric | Fixed this session (lon buffer × cos(lat)) |
| 27 | CanonicalStation.Geometry always null | Populated on creation (both sites) this session |
| 33 | PickupType/DropOffType invalid vs null | Validation logging added this session |
| 35 | FeedVersion missing AgencyCount | Added to response this session |
| 37 | GetDeparturesAsync truncates fractional seconds | Math.Round added this session |
| 46 | GtfsParserManager is not a Manager | Renamed to GtfsParser this session |
| 47 | ImportLogStore in Managers namespace | Fixed this session |
| 48 | EncodeGeohash off-by-boundary | Comment added this session |
| 49 | NormalizeName vs ToNameSlug divergence | Unified this session |
| 50 | PlaceMatchingManager static methods | Extracted static utility this session |
| 56 | FeedVersion.IsActive no unique filtered index | Migration applied this session |
| 57 | MobilityManager registration | Fixed in prior work |
| 59 | feeds.html health check sequential | Batch 5→20 this session |
| 60 | reconciliation.html batch approve serial | Promise.all parallelism this session |
| 61 | Map icon flash on first render | Preload icons this session |
| 62 | Vehicle bearing font fallback | SVG marker this session |
| 63 | ParseCalendarDates error normalization | Normalized this session |
| 66 | CanonicalRoute GetRoutes no limit | .Take(500) added this session |
| 67 | SqlBulkCopy shared connection rollback risk | Separate connection this session |
| 68 | RealtimeManager loads full protobuf | Streaming this session |
| 73 | appsettings.json thresholds differ from code | Already 100/300 matching code defaults |
| 74 | TriggerImportAsync passes CancellationToken.None | Already passes caller's ct |
| 75 | SearchAsync total count ignores stationType | Already passes stationType to GetTotalCountAsync |
| 76 | MobilityProvider has no Name property | Already has string Name |
| 77 | Alert affected IDs never populated | Already populated from InformedEntity |
| 78 | 1-day cutoff instead of 7-day | Already AddDays(-7) |
| 79 | Cross-midnight departure wrong date | Already handled in display code |
| 81 | Manual SHA1 collision within same second | Already includes Guid |
| 82 | MergeStationsAsync route conflict check | Route-set mismatch downgraded from error to warning this session |
| 86 | Route type colors duplicated in 5 places | Colors swapped (bus=green, tram=blue) this session |
| 94 | ImportLogStore entries lost on restart | Documented as in-memory limitation this session |
| 99 | Bidirectional route GroupBy undefined | Documented this session |
| 101 | StationMergeLog MovedRawStopIds CSV string | Join table created + migration applied this session |
| 104 | CreatedAt inconsistency / missing on Alert | CreatedAt added this session |
| 106 | ComputeGtfsSha1 loads all files into memory | Streamed to hasher this session |
| 108 | Calendar validation says optional but import requires | Calendar/calendar_dates required in ValidateGtfs this session |
| 110 | GeoJSON station endpoint calls wrong URL | URL fixed this session (/stations not /stations/search) |
| 132 | Migration Down() nullable column | Fixed this session |
| 134 | ReconciliationController defaults conflict | Already match config (100/300) |
| 138 | No XML docs on contracts | Added this session |
| 139 | launchSettings HTTP only | HTTPS profile added this session |

---

## Deferred / Blocked (no phase)

| # | Issue | Reason |
|---|-------|--------|
| 26 | No re-reconciliation endpoint | Non-trivial feature, deferred |
| 36 | CanonicalRoute GlobalId embeds FeedId | Policy decision — FeedId as permanent identifier |
| 87 | GBFS polling is no-op stub | Skipped — no active GBFS providers |
| 89 | MobilityProvider.ApiKey stored in plain text | Skipped — none in use |
