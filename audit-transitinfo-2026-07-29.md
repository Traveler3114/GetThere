# TransitInfoAPI — Full Audit

**Date:** 2026-07-29 · **Scope:** `TransitInfoAPI` — 147 files, ~16 500 lines
**Status: all 28 findings resolved.** Reconciliation was also moved outside the import transaction
(T18) at the owner's decision, which required a new version state and a repair endpoint rather than
the two-line move originally estimated.

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

## Coverage

| Area | Coverage |
|---|---|
| All managers, services, `Data/`, workers, `Program.cs`, `Common/` | **read in full** |
| Controllers | routes, authorization, parameter binding reviewed (thin pass-throughs) |
| Entities, Contracts, Mapping | reviewed structurally |
| `wwwroot/admin` (~3 000 lines JS) | reviewed for defect classes — escaping verified sound. **Not read line-by-line — the remaining gap** |
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

## Remaining

Nothing outstanding from this audit. Two things worth a future pass:

1. **`wwwroot/admin` line-by-line** — reviewed for defect classes, escaping verified, but ~3 000
   lines not read directly.
2. **Watch `ReconciliationPending`** in the admin console. It should stay empty; a version sitting in
   it means reconciliation failed after a successful import and needs the repair endpoint.
