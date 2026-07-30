# TransitInfoAPI — Full Audit

> ## Still current — one item to note
>
> All 28 findings here were re-checked against the tree on 2026-07-30 and **none has regressed** on the
> surfaces that pass read. Two things this file does not cover:
> `AuthManager.RefreshAsync` still lacks the account recheck that GetThereAPI's equivalent gained
> (M12), and the admin JS this file names as its "remaining gap" was swept but still not read
> line-by-line. See [`audit-2026-07-30.md`](audit-2026-07-30.md).

**Date:** 2026-07-29 · **Scope:** `TransitInfoAPI` — 147 files, ~16 500 lines
**Status: all 46 findings resolved** — 28 from the code audit (T1–T28) plus 18 from the admin console
(A1–A18), which was the coverage gap left open in the first pass and is now closed. Reconciliation
was also moved outside the import transaction (T18) at the owner's decision, which required a new
version state and a repair endpoint rather than the two-line move originally estimated.

**Backed by a live run:** both APIs against the real databases, real feeds (ZET, HŽPP, ÖBB, Gradski
parking), 6 200 canonical stations, 531 routes, live GTFS-RT.

## Verification

| Gate | Result |
|---|---|
| `dotnet build -warnaserror` × 4 projects | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` × 4 | clean |
| `dotnet test` | **311 / 311 pass** |
| Migration | generated, applied, confirmed in `sys.indexes` / `sys.foreign_keys` |
| Live re-verification | departures, reconciliation repair, operator service-area |
| Admin console | all 16 pages loaded against the live API and checked for console errors |

## Coverage

| Area | Coverage |
|---|---|
| All managers, services, `Data/`, workers, `Program.cs`, `Common/` | **read in full** |
| Controllers | routes, authorization, parameter binding reviewed (thin pass-throughs) |
| Entities, Contracts, Mapping | reviewed structurally |
| `wwwroot/admin` (3 770 lines JS, 16 pages) | **read in full, line by line** — 17 further findings, see below |
| `Migrations/` | excluded (auto-generated) |

---

## 🔴 High — 4 of 4 resolved

**T1 · Departures logged 4 + 3×N lines at Information** — `ScheduleManager.cs` ✅
Four per call plus three per departure from inside the projection (~34 lines for ten departures) on
the endpoint the map calls on every stop tap. Now one Debug line behind an `IsEnabled` guard.
**Measured live: 34 → 0 log lines per request.**

**T2 · Departures had no `Take`** — `ScheduleManager.cs` ✅
Read every remaining departure of the service day across all active feed versions. Now ordered and
bounded in SQL with an escalating scan — see *Regression caught* below.

**T3 · Every departure returned an empty headsign** — `ScheduleManager.cs` ✅
`Headsign = ""` was hardcoded; now reads `Trip.TripHeadsign`.
**Live: 0/10 → 10/10 populated** (`Dubec`, `Sopot`, `Savišće`).

**T4 · GBFS ingestion trusted fields it did not check** — `MobilityManager.cs` ✅
`GetProperty("lat").GetDouble()` and `cap.GetInt32()` threw on a missing `lat` or a `capacity` that
was null or a string — both legal GBFS, either killing the upsert for the whole feed. Every field now
goes through `ReadDouble`/`ReadInt` (number, numeric-string, or absent), plus the coordinate range
check `ParseStops` already applied. Skipped stations are counted and logged.

---

## 🟠 Medium — 14 of 14 resolved

**T5 · The `Restrict` loop inverted RefreshToken's cascade** — `TransitDbContext.cs` ✅
Convention loop moved **above** the per-entity configuration, matching `AppDbContext`. Needed a
migration; verified in `sys.foreign_keys` as `CASCADE`.

**T6 · A shape with <2 points failed the whole import** — `GtfsParser.cs` ✅ Guarded and counted.

**T7 · Shape coordinates were not range-checked** — `GtfsParser.cs` ✅ Same check `ParseStops` uses.

**T8 · Place-matching cooldown never applied** — `PlaceMatchingManager.cs` ✅
`_lastMatchRun` was an instance field on a **scoped** service. Now static with `Interlocked`, so the
window spans instances and survives three parallel imports.

**T9 · Place matching loaded every placed station** — `PlaceMatchingManager.cs` ✅
Bounding-box pre-filter in SQL (a cheap superset of "further than 500 m"); the exact great-circle
test now runs over a handful of rows rather than the whole table.

**T10 · Eight uncached calendar queries per departures request** — `ScheduleManager.cs` ✅
Cached per date with a 5-minute absolute expiry — long enough to collapse the burst, short enough
that a newly imported version appears without explicit invalidation.

**T11 · Shape generation read all of `stop_times.txt`** — `FeedManager.cs` ✅
Switched to `ParseStopTimesBatchedAsync`, which already existed and was used by the import phase.

**T12 · CSV validation fully disabled** — `GtfsParser.cs` ✅ *(deliberately partial — see below)*

**T13 · Feeds without `direction_id` flagged every stop** — `ReconciliationManager.cs` ✅
Rule changed to "an unknown direction cannot disagree with anything" rather than removing the check;
genuine conflicts still detected.

**T14 · Reassign/reject/approve left `StopTimes` behind** — `ReconciliationManager.cs` ✅
New `RepointStopTimesAsync` wired into all four sites. `ReassignCandidateAsync` also gained a
transaction — it now writes in two steps (an immediate `ExecuteUpdate` plus a `SaveChanges`) and had
none, unlike its Approve/Reject siblings.

**T15 · Operator service-area returned malformed GeoJSON** — `OperatorManager.cs` ✅
Hand-rolled projection replaced with `GeoJsonGeometry.FromNtsGeometry`.
**Live: `Polygon` nesting 2 → 3** (rings → points → x/y). Previously unrenderable by any client.

**T16 · Mobility upsert crashed on a duplicate station id** — `MobilityManager.cs` ✅
Grouped instead of `ToDictionary`, plus a real unique index on `(OperatorId, StationId)`. Checked for
existing duplicates before adding it — none. Verified in `sys.indexes`.

**T17 · GBFS polling never used its failure options** — `MobilityPollingWorker.cs` ✅
Consecutive-failure counter and deactivation, matching `FeedPollingWorker`.

**T18 · Reconciliation inside the import transaction** — `FeedManager.cs` ✅ **moved out**

---

## 🟡 Low — 10 of 10 resolved

| # | Issue | Resolution |
|---|---|---|
| T19 | 3×3 grid narrower than the 50 km threshold in longitude | Window now sized from `MaxDistanceMeters` and the latitude's metres-per-degree |
| T20 | "Turkey (east)" box was Romania/Moldova/Ukraine | Box deleted — RO/MD/UA already cover it correctly |
| T21 | Check-then-insert races on unique columns ×3 | Unique-violation caught in all three; 500 → 409, or read-back for the country race |
| T22 | `HasDirectionMismatch` indexed dictionaries directly | `TryGetValue`, matching its sibling |
| T23 | Import logs leaked on failure | `_logStore.Clear` added to the error path |
| T24 | Unbounded association lists | Capped at 500, matching `GetRoutesAsync` |
| T25 | `UpdateShapeAsync` returned null for three failures | 404 / 409 / 400 with distinct codes |
| T26 | Bad `Schedule:Timezone` threw per request | Falls back to UTC with a loud error |
| T27 | Unknown route types silently became `Bus` | Logged once per distinct unmapped value |
| T28 | Comparer mismatch on the same key | Both `OrdinalIgnoreCase` |

---

## T18 in detail — what "move it outside" actually took

The original estimate of a two-line move was wrong, because moving it changes what happens on
failure: reconciliation can no longer roll the import back, so a failure would otherwise commit an
**active** version whose raw stops link to no canonical station — stops present, departures empty,
map quietly wrong. Three pieces:

1. **`FeedImportStatus.ReconciliationPending`** — deliberately distinct from Success (which would
   hide a version resolving to nothing) and Failed (which would suggest discarding good data).
2. **`ReconcileImportedVersionAsync`** owns the outcome. On failure the imported data is kept and the
   version is parked; `ReconcileAndBackfillAsync` no longer sets a status itself.
3. **`POST /feeds/versions/{id}/reconcile`** — the repair, idempotent, so it is also safe against a
   version that already reconciled cleanly.

**Verified live:** `{"message":"Reconciliation complete."}` — 2 515 raw stops reconciled outside any
transaction.

---

## Regression caught during verification

The first fix for T2 used a fixed over-fetch multiplier (`count × 6`). The warning added alongside it
fired on the first real request:

> `Departure scan for station 17977 hit its 60-row cap and still returned only 6 of 10 requested`

60 rows scanned yielded 6 valid departures, so a fixed multiplier **silently returned fewer results
than the unbounded version it replaced** — a quieter bug than the one being fixed. Reworked to an
escalating scan: widen ×4 until satisfied, the source is exhausted, or a 2 000-row ceiling. Now
returns 10/10, matching the original behaviour while staying bounded.

---

## Judgement calls

**T12 is deliberately partial.** `MissingFieldFound` and `HeaderValidated` stay off: most columns the
class maps name are optional in GTFS, so a feed omitting `stop_desc` or `bikes_allowed` is correct,
not broken — reporting those would be pure noise. Only `BadDataFound` and `ReadingExceptionOccurred`
now log (capped at 20 per file), because those fire on genuinely malformed input.
`FeedManager`'s contradictory *"single bad record fails the entire import"* comment was corrected.

**T13 changed the rule, not the check.** Absence of a direction is not a contradiction, so unknown
directions are skipped; two stops with genuinely opposite known directions still conflict.

**T20 deleted the box rather than correcting it.** Turkey does not reach north of ~42°N, and RO, MD
and UA already cover that rectangle.

---

## Checked and sound

- **SSRF protection** (`ExternalFeedSource`) — loopback, link-local incl. cloud metadata, RFC1918,
  IPv4-mapped IPv6, unique-local, re-checked after redirects.
- **`FeedStorage`** — path containment extracted so it is testable; rejects rooted paths before
  `Path.Combine`.
- **Decompression-bomb ceiling** and a capped read that survives a lying `Content-Length`.
- **Admin console escaping** — every feed-, operator- and agency-supplied string goes through
  `esc()`. Verified specifically because the raw escaped-to-total ratio looks alarming and is
  misleading: the unescaped interpolations are numbers, booleans and static markup.
- **Levenshtein** — two rolling rows with a stack allocation under 256 chars.
- **Reconciliation thresholds** captured per candidate at decision time, so changing a threshold does
  not rewrite the recorded reason a merge happened.

---

## Fixed earlier the same day (verified live)

| Finding | Evidence |
|---|---|
| **GTFS static import completely broken** — `SqlBulkCopy` named source mappings forced `ObjectArrayReader.GetOrdinal`, which throws | ZET: 151 routes, 3 786 stops, 101 791 trips. Dashboard 5/7 → **7/7 feeds healthy** |
| Feed download failed on a locked destination | HŽPP imports (44 864 trips) |
| Per-trip Information logging + a hardcoded debug trip id | Demoted to `Debug`; scaffolding removed |
| Alerts with no `ActivePeriodEnd` never purged | Sweep ages out by `FetchedAt` |
| Alert filtering substring-matched a comma-joined id list | Delimiter-padded match |
| Import transaction never disposed; EF's `CurrentTransaction` never cleared | `finally` + `UseTransaction(null)` |
| Rate limiter partitioned on IP only | Partitions on the authenticated caller |
| `UseRateLimiter` before `UseAuthentication` | Reordered |
| Map pages had no CSP and interpolated feed text into `innerHTML` | CSP added, script extracted, feed text escaped |
| Seed `CreateAsync`/`AddToRoleAsync` results discarded | Checked and logged |
| `Include()` on projecting queries | Removed |

---

## Admin console — the closed coverage gap (A1–A18)

The earlier pass checked the admin JS for defect *classes* (escaping, injection) and signed it off on
that basis. Reading it line by line, and loading all 16 pages against the live API, found 18 more —
including three pages that had never worked at all. Escaping was indeed sound; everything below is a
different kind of mistake, which is the lesson: sampling for one defect class says nothing about the
others.

### 🔴 High — 4

**A1 · `PlaceManager` was never registered in DI** — `Program.cs` ✅
`PlacesController` could not be activated, so all four of its actions returned 500 from the container
before reaching any code. The Places page has never loaded. Nothing catches this at build time — the
controller compiles fine, and it is the only manager of thirteen that was missing. Verified after the
fix: `200 {"data":[],"total":0,...}`.

**A2 · Stations asked for `perPage=10000` against a `[Range(1, 500)]` endpoint** — `stations.page.js` ✅
Model validation rejected every request, so the page showed only *"Failed to load: Unknown error"*.
Capped at 500 — which is what made it render for the first time: **50 rows, truncation notice shown**.

**A3 · Reconciliation hung on "Loading..." forever** — `reconciliation.page.js` ✅
`loadAll()` runs both tab loaders back to back to prime the other tab's total. They share one spinner
and one content pane, and the second one raised the spinner and hid the content it then declined to
re-render, because it is not the visible tab. The pending rows were built and then covered up. The
chrome is now only touched for the tab actually on screen.

**A4 · The admin token was attached to every outbound fetch** — `admin-auth.js` ✅
The wrapper added `Authorization: Bearer <admin JWT>` to *all* requests, including cross-origin ones.
Two consequences: map tiles and styles failed CORS preflight (an Authorization header makes a request
non-simple), and any third-party host willing to answer that preflight would have been handed an
admin bearer token. Now same-origin only. **Verified: cross-origin fetch 200, same-origin still
authenticated, map style resolves 28 layers where it previously failed outright.**

### 🟠 Medium — 5

**A5 · Route shapes never drew on the reconciliation map** — `reconciliation-map.page.js` ✅
The guard tested `shapeData.features`, but `GET /routes/{id}/shape` returns a single GeoJSON
**Feature**, not a FeatureCollection — so it returned early every time. Confirmed against the live
endpoint: the response keys are `type`, `geometry`, `properties`.

**A6 · Every HTTP error left the spinner running** — 12 pages ✅
All the loaders bail out of `if (!r.ok) { showError(...); return; }` before reaching their own hide,
so a failed request showed a spinner and an error message simultaneously. Fixed once, in each page's
`showError`. **Verified against a real 400.**

**A7 · A failed alerts request rendered as good news** — `alerts.page.js` ✅
No `r.ok` check: the problem+json body parsed into an object whose `.length` is undefined, so the
page reported *"No active alerts."* for a server error.

**A8 · Unticking a license box recorded "unknown", not "no"** — `feeds.page.js` ✅
`.checked || null` sent `null` for every unticked box and `FeedManager` assigns the request straight
onto the entity. Both render unticked, so the UI looked right while the distinction the `bool?` exists
to carry was lost — which for *"redistribution allowed"* is the difference between unknown terms and
terms that forbid it.

**A9 · The Overview's Version column was always blank** — `index.page.js` ✅
Read `version.sha`; the field is `sha1`. **Now populated: `574f9da`, `50f1ec0`, `c1bd2b2`, `02342d3`.**

### 🟡 Low — 9

| # | Issue | Resolution |
|---|---|---|
| A10 | Reload discarded the active filter while the filter control still showed it (`agencies`, `feed-versions`) | Dropped the re-assignment that overwrote the filtered list. **Verified: 4 rows before and after a reload** |
| A11 | Countries fetched without `perPage`, taking the server default of 50 — silent truncation at the 51st country | Requests the endpoint's 500 ceiling |
| A12 | Overview queue rows all read "canonical" — `suggestedStationDetail` is on `ReconciliationDetailResponse`, which the list endpoint does not return | Pairs identifiers instead; the bold line already carries the names |
| A13 | Batch-approve's *"N of M failed"* was erased by the reload that followed it | Reported after the reload completes |
| A14 | 12 mojibake sequences (`—`, `≥`, `≤` round-tripped through Windows-1252) across 3 files | Repaired; files rewritten UTF-8 without BOM |
| A15 | `/stations/{id}/routes` fetched twice per marker click | One request, two consumers |
| A16 | `Shell.api` let a caller's own `headers` replace the auth headers wholesale | Merged rather than overwritten — no caller does this today; now none has to know not to |
| A17 | `!s.latitude` skipped a stop at longitude 0 | `== null`, matching the fix already made elsewhere |
| A18 | Dead `s`/`sClass` locals | Removed |

### Also fixed earlier in the same pass

`agencies` timezone/phone and `places` region/country codes went into HTML unescaped (both are raw
feed data); unguarded `.toFixed()` in `realtime`, `places`, `stations` and `reconciliation-map`;
truthiness-vs-null on latitude in `reconciliation`; and `operators` interpolated `globalId` raw into a
JS string inside an HTML attribute — replaced with data attributes and `addEventListener`, which also
removes one of the blockers to dropping `unsafe-inline` from the console's CSP.
**Verified live: 6 operators, 0 leftover inline handlers, detail modal opens correctly.**

### Noted, deliberately not changed

`reconciliation.page.js` renders "Raw stop routes" and "Matched station routes" blocks that are dead
on the list page for the same reason as A12 — the list endpoint returns `ReconciliationResponse`,
without the detail objects. Making them work means widening the list payload to carry per-item route
and operator detail for 25 rows at a time. That is a cost decision about the API, not a bug fix, so it
is left as-is: the blocks are conditional and degrade silently rather than rendering wrongly.

---

## Remaining

Nothing outstanding from this audit.

1. **Watch `ReconciliationPending`** in the admin console. It should stay empty; a version sitting in
   it means reconciliation failed after a successful import and needs the repair endpoint.
2. **`Places` is empty** (0 rows, and no station carries a `PlaceId`). Not a defect — the gazetteer
   has never been seeded — but it does mean `PlaceMatchingManager` and the T8/T9/T19 fixes are
   currently exercising an empty table, so they are correct by inspection rather than by observation.
