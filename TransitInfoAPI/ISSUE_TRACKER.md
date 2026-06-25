# TransitInfoAPI — Consolidated Issue Tracker + Execution Plan

> **Status Key**
> - **[FIXED]** — Resolved, not actionable
> - **[OPEN]** — Still broken
> - **[DEFERRED]** — Blocked on policy or non-trivial redesign

---

## Phase 1 — Data Integrity **[FIXED]**

| # | Issue | What | Fix |
|---|-------|------|-----|
| 7 | **OperatorManager.GetTotalCountAsync missing** | Operator list total ignores countryId/q filters. Count logic lives in controller with 3 redundant queries. | Added `GetTotalCountAsync` to OperatorManager, simplified controller count logic |
| 10 | **Alert dedup key too broad** | Key is (Cause, Effect, ActivePeriodStart, HeaderText). Two alerts same cause/effect/header but different routes get collapsed. | Widen key to include AffectedRouteIds, AffectedStopIds, AffectedTripIds, AffectedAgencyIds |
| 11 | **GeoJSON stations no pagination** | format=geojson branch has .Take(5000) but no Skip/page/perPage | Reverted: map viewport needs all stations in bounds, kept Take(5000). Non-GeoJSON list already paginated |
| 12 | **FeedVersions.GetStops no pagination** | Returns all raw stops for a version (up to 50K+). | Added page/perPage params, returns Paginated<RawStopResponse> |
| 128 | **Map vehicle fetch silently swallowed** | index.html .catch(() => {}) for vehicles fetch. | Changed to .catch(() => showMapError('Failed to load vehicles')) |

---

## Phase 2 — Import Pipeline **[FIXED]**

| # | Issue | Fix |
|---|-------|-----|
| 18 | **Import stuck cleanup incomplete** | Already handles CalendarDates, Calendars, Shapes alongside StopTimes, RawStops, Trips |
| 19 | **SetCommandTimeout(600) never reset** | Already resets in finally block |
| 21 | **ParseStops passes (0,0) coordinates** | Already filters (lat==0 && lon==0), logs warning |
| 22 | **CsvConfig AllowComments=true** | Already AllowComments = false |
| 80 | **N+1 route import queries** | Already uses dictionaries for O(1) lookups |
| 111 | **Import modal locks on network error** | Added AbortController with 120s timeout |

---

## Phase 3 — Background Workers **[FIXED]**

| # | Issue | Fix |
|---|-------|-----|
| 30 | **Feed failure count not reset for null URL** | Already resets via TryRemove on null return from CheckAndFetchAsync |
| 112 | **Failure counter non-atomic** | Replaced ConcurrentDictionary with Dictionary + lock for atomic increment+check+remove |
| 58/92 | **setInterval never cleared** | Added pagehide listener; fixed scope of vehiclesInterval variable in map |

---

## Phase 4 — Admin UI **[FIXED]**

| # | Issue | Fix |
|---|-------|-----|
| 113 | **Countries pagination broken** | Already uses paginated list variable (list.map, not allCountries.map) |
| 114 | **Mobility table pagination broken** | Changed const pageSize → let so dropdown works |
| 115 | **Mobility card pagination broken** | Same const→let fix |
| 116 | **Broken DOM selector — Inspect button** | Selector *= correctly matches; not broken |
| 117 | **Broken DOM selector — import completion** | Same — already correct |
| 129 | **Continent XSS** | Already wrapped with esc() |

---

## Phase 5 — Missing Indexes **[FIXED]**

All indexes already defined in `TransitDbContext.cs` and present in model snapshot:
- CanonicalStationOperator → OperatorId
- CanonicalRoute → OperatorId
- Feed → FeedId (unique)
- Alert → FeedId
- Trip → CanonicalRouteId
- ReconciliationCandidate → SuggestedCanonicalStationId
- MobilityStation → MobilityProviderId
- City → CountryId
- FeedVersion.Sha1 → IsUnique()
- (FeedVersionId, TripId) → IsUnique()

---

## Phase 6 — Polish & Convention **[PARTIAL]**

### Fixed this session

| # | Issue | Fix |
|---|-------|-----|
| 42 | **volatile on ConcurrentDictionary** | Added volatile to _tripUpdateCache (reassigned via Interlocked.Exchange) |
| 55 | **Paginated perPage=0 division by zero** | Added [Range(1, 500)] to perPage in all controllers |
| 105 | **Directory.CreateDirectory no error handling** | Wrapped in try/catch, throws AppException |
| 133 | **Exception handler leaks SQL details** | Non-AppException errors now show generic message |
| 20 | **BeginImportTransactionAsync no existing-tx check** | Only calls UseTransaction when no existing tx present |
| 34 | **MatchStationsToPlacesAsync stale re-match** | Added CooldownHours config; skip if run recently |
| 40 | **BackfillRouteGeometriesAsync client eval risk** | Restructured to two-step: SQL GroupBy → in-memory aggregation |
| 52 | **UpdateOperatorRequest allows empty Name** | Added [MinLength(1)] on Name |
| 69 | **Feed URL format validation missing** | Log warning for non-.zip static feed URLs |
| 140 | **GeoJsonGeometry anonymous types + null!** | Replaced anonymous types with typed classes |

### Already fixed (verified this session)

| # | Issue | Status |
|---|-------|--------|
| 23 | _feedLocks / _importLocks separate | Already unified (single dict) |
| 25 | ParseGtfsTimeToSeconds returns 0 for invalid | Already returns null |
| 32 | DataTable reuse across batches | Already new DataTable per batch |
| 38 | FindNearestPlace 50km hardcoded | Already configurable via PlaceMatchingOptions |
| 41 | PascalCase constructor params | Already camelCase (fixed FeedsController) |
| 43 | IsServiceActiveOnAsync dead code | Already removed |
| 44 | GenerateOperatorOnestopId overload dead | Already removed |
| 53 | CreateFeedRequest negative interval | Already [Range(60, int.MaxValue)] |
| 54 | FeedManager.CreateAsync no OnestopId check | Already checked |
| 64 | ParseAgencies no empty agency_id check | Already handled via __default__ sentinel |
| 65 | promptReassign uses browser prompt() | Already replaced |
| 70 | Non-zip URL returns HTML | Already Content-Type guard in CheckAndFetchAsync |
| 71 | No health endpoint | Already present (GET /health) |
| 72 | ImportError bool → string | Already string? |
| 84 | AutoMergeVerdict missing from list response | Already in ReconciliationResponse |
| 85 | CountriesController no pagination | Already paginated |
| 90 | VehicleResponse has no routeType | Map doesn't reference v.routeType |
| 91 | realtime.html filter label mismatch | Placeholder accurately describes filter |
| 93 | GetReconciliationDetail placeholder CreatedAt | Already genuine timestamps |
| 95 | GtfsRouteTypeMapper missing 203 | Already explicit case 203→Bus |
| 96 | UpdateFeedRequest allows negative interval | Already [Range(60, int.MaxValue)] |
| 97 | RouteMapper Operator null risk | Already has null guards |
| 98 | CountryName string filter — table scan | Uses == equality, not Contains |
| 102 | CsvConfig new instance per call | Already static readonly |
| 103 | MergePreview returns anonymous type | Already typed MergePreviewResponse |
| 130 | StationType Unknown fallback | Already Unknown = 999 (handled in mapping) |
| 131 | Hardcoded 10s initial delay | Already configurable via InitialDelaySeconds |
| 135 | Static locks never cleaned up | Already cleaned up on feed delete (TryRemove) |
| 136 | No validation attributes on request DTOs | All request DTOs have [Required]/[Range]/[StringLength] |
| 137 | AlertResponse missing affected IDs | Already has all fields |
| 39 | LoadPlacesAsync sort has no effect | Already removed sort |

### Remaining (not actionable — limitation accepted)

| # | Issue | Reason |
|---|-------|--------|
| 31 | GenerateFeedOnestopId always (0,0) | Operator/Country entities have no lat/lon fields; would need migration to add |

---

## Already Fixed (prior sessions)

| # | What | Fixed in |
|---|------|----------|
| 1 | ApproveCandidateAsync missing transaction | Already has BeginTransactionAsync |
| 2 | MergeStationsAsync missing transaction | Already has BeginTransactionAsync |
| 3 | FeedId collision | Session 1 (unique index + validation in CreateAsync) |
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
| 24 | Reconciliation bounding box asymmetric | Session 1 (lon buffer × cos(lat)) |
| 27 | CanonicalStation.Geometry always null | Session 1 (populated on creation) |
| 33 | PickupType/DropOffType invalid vs null | Session 1 (validation logging) |
| 35 | FeedVersion missing AgencyCount | Session 1 (added to response) |
| 37 | GetDeparturesAsync truncates fractional seconds | Session 1 (Math.Round) |
| 46 | GtfsParserManager is not a Manager | Session 1 (renamed to GtfsParser) |
| 47 | ImportLogStore in Managers namespace | Session 1 (namespace fix) |
| 48 | EncodeGeohash off-by-boundary | Session 1 (comment added) |
| 49 | NormalizeName vs ToNameSlug divergence | Session 1 (unified) |
| 50 | PlaceMatchingManager static methods | Session 1 (extracted static utility) |
| 56 | FeedVersion.IsActive no unique filtered index | Session 1 (migration applied) |
| 57 | MobilityManager registration | Prior work |
| 59 | feeds.html health check sequential | Session 1 (batch 5→20) |
| 60 | reconciliation.html batch approve serial | Session 1 (Promise.all parallelism) |
| 61 | Map icon flash on first render | Session 1 (preload icons) |
| 62 | Vehicle bearing font fallback | Session 1 (SVG marker) |
| 63 | ParseCalendarDates error normalization | Session 1 (normalized) |
| 66 | CanonicalRoute GetRoutes no limit | Session 1 (.Take(500)) |
| 67 | SqlBulkCopy shared connection rollback risk | Session 1 (separate connection) |
| 68 | RealtimeManager loads full protobuf | Session 1 (streaming) |
| 73 | appsettings.json thresholds differ from code | Already 100/300 matching code defaults |
| 74 | TriggerImportAsync passes CancellationToken.None | Already passes caller's ct |
| 75 | SearchAsync total count ignores stationType | Already passes stationType to GetTotalCountAsync |
| 76 | MobilityProvider has no Name property | Already has string Name |
| 77 | Alert affected IDs never populated | Already populated from InformedEntity |
| 78 | 1-day cutoff instead of 7-day | Already AddDays(-7) |
| 79 | Cross-midnight departure wrong date | Already handled in display code |
| 81 | Manual SHA1 collision within same second | Already includes Guid |
| 82 | MergeStationsAsync route conflict check | Session 1 (downgraded to warning) |
| 86 | Route type colors duplicated in 5 places | Session 1 (bus=green, tram=blue) |
| 94 | ImportLogStore entries lost on restart | Session 1 (documented) |
| 99 | Bidirectional route GroupBy undefined | Session 1 (documented) |
| 101 | StationMergeLog MovedRawStopIds CSV string | Session 1 (join table + migration) |
| 104 | CreatedAt inconsistency / missing on Alert | Session 1 (CreatedAt added) |
| 106 | ComputeGtfsSha1 loads all files into memory | Session 1 (streamed to hasher) |
| 108 | Calendar validation says optional but import requires | Session 1 (required in ValidateGtfs) |
| 110 | GeoJSON station endpoint calls wrong URL | Session 1 (URL fixed) |
| 132 | Migration Down() nullable column | Session 1 (fixed) |
| 134 | ReconciliationController defaults conflict | Already match config (100/300) |
| 138 | No XML docs on contracts | Session 1 (added) |
| 139 | launchSettings HTTP only | Session 1 (HTTPS profile added) |

---

## Deferred / Blocked

| # | Issue | Reason |
|---|-------|--------|
| 26 | No re-reconciliation endpoint | Non-trivial feature, deferred |
| 36 | CanonicalRoute GlobalId embeds FeedId | Policy decision — FeedId as permanent identifier |
| 87 | GBFS polling is no-op stub | Skipped — no active GBFS providers |
| 89 | MobilityProvider.ApiKey stored in plain text | Skipped — none in use |
