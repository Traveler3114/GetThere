# TransitInfoAPI — Station Reconciliation

This is the hardest problem in the system and the reason the service exists in its current form.

## The problem

Four operators serve Zagreb main station. Their feeds say:

| Operator | `stop_id` | `stop_name` | lat, lon |
|---|---|---|---|
| HŽPP | `1001` | `Zagreb Glavni kolodvor` | 45.8046, 15.9786 |
| ÖBB | `8100108` | `Zagreb Hbf` | 45.8043, 15.9791 |
| ZET (tram) | `216` | `Glavni kolodvor` | 45.8051, 15.9780 |
| Autotrolej | `ZG-GK` | `Zagreb Gl. kol.` | 45.8047, 15.9788 |

**Nothing in GTFS says these are related.** IDs are per-feed, names are inconsistent, coordinates
differ by tens of metres because each operator surveyed their own platform.

A user searching "Zagreb" must get *one* station. A ticket referencing that station must keep meaning
the same place after the next feed import. Two things follow:

1. There must be an identity **above** the feed: `CanonicalStation`.
2. Assigning raw stops to it must be automatic where safe, and reviewable where not.

## The two-layer model

```
RawStop              CanonicalStation
────────────────     ─────────────────────
what a feed said     the real-world place
per FeedVersion      stable across feeds and re-imports
immutable history    what clients query
                     has the OnestopId
```

`RawStop.CanonicalStationId` is the link, and `RawStop.ReconciliationStatus` records how it got there.

Everything external — the map, GetThereAPI, an imported ticket's `OperatorGlobalId` — refers to
canonical stations. Raw stops are internal provenance.

---

## OnestopId: deterministic identity

Before any fuzzy matching, `OnestopIdManager` gives every stop a **deterministic** ID derived from its
own properties:

```
s-{geohash9}-{name-slug}~{routetype}

s-u2ndz1mwq-zagreb-glavni-kolodvor~train
```

| Part | Meaning |
|---|---|
| `s-` | Entity type — `s` stop, `o` operator, `r` route, `f` feed |
| `geohash9` | 9-character geohash ≈ 5 m precision |
| `name-slug` | Normalised name, diacritics folded, abbreviations expanded, ≤64 chars |
| `~routetype` | `train`, `tram`, `bus`, `ferry`, … |

The point of a deterministic ID is that **re-importing the same feed produces the same IDs**, so
reconciliation is idempotent — the second import matches on OnestopId and does no fuzzy work at all.
It also gives external systems a stable string to store. GetThereAPI holds one in every imported
ticket's `OperatorGlobalId`.

### Why the route type is in the ID

A tram stop and a bus stop at the same kerb are genuinely different places to a traveller — different
vehicles, often different platforms. Including the route type keeps them distinct without any
special-casing. It is also why `RawStop.RouteType` must be populated *before* reconciliation runs,
which is why stop times are imported before stops (see
[feed-pipeline.md](feed-pipeline.md#importing-importfeedversionasync)).

### The geohash boundary limitation

```csharp
// Two stops 6m apart at a geohash boundary produce different geohashes and thus different
// OnestopIds. The 20m proximity fallback in reconciliation catches most cases.
// Acceptable known limitation.
```

Geohash cells are a grid, so two adjacent stops can straddle a boundary and get different IDs despite
being metres apart. The fallback below catches most of it. The limitation is documented rather than
solved, and it is the reason the exact-ID path is not sufficient on its own.

### Name normalisation

`ToNameSlug` folds Croatian and general Latin diacritics (`č ć → c`, `š → s`, `ž → z`, `đ → d`, plus
accented vowels) and expands local abbreviations:

```
kol → kolodvor    ul → ulica    st → sveti    sv → sveti
```

Domain knowledge, not general text processing: `kol.` for *kolodvor* (station) is what Croatian
operators actually write. It is also why this codebase is not portable to another country's feeds
without revisiting the abbreviation table.

---

## The matching pipeline

`ReconcileFeedVersionAsync` runs at the end of each import, over the version's active raw stops of
type `Stop`.

### Bounding box, adjusted for latitude

Only canonical stations near the feed's extent are loaded:

```csharp
var latBuffer = 0.001;
var lonBuffer = latBuffer / Math.Cos(centerLat * Math.PI / 180);
```

Longitude degrees narrow toward the poles, so a fixed degree buffer would be geographically smaller
in longitude than latitude. Dividing by `cos(lat)` makes the buffer roughly square on the ground.

### Phase 1 — exact and near-exact assignment

For each raw stop, in order:

1. **OnestopId already seen this pass** → reuse that station. Handles a feed listing several platforms
   that collapse to one identity.
2. **An *inactive* station has this OnestopId** → **reactivate it** rather than creating a duplicate.
   This is what makes a feed that disappears and returns not fragment the station's identity — and it
   preserves any merge history attached to it.
3. **A nearby station within 20 m with ≥0.85 name similarity and the same route type** → assign. This
   is the geohash-boundary fallback.
4. Otherwise **create a new `CanonicalStation`**, deriving its country from coordinates.

### Phase 1.5 — build route and direction lookups

The feed's trips are indexed so the next phase can ask *which lines serve this stop, in which
direction*. This is what raises matching above name-and-distance.

### Phase 2 — scored matching

Existing stations already linked to **this operator** are excluded from candidacy:

```csharp
// Within-operator platforms (different tracks/stops at the same station) should
// keep distinct CanonicalStations. Auto-merge is only for cross-operator dedup,
// e.g. OBB's "Zagreb Glavni Kolodvor" merging with HZPP's.
```

This is the key rule. Collapsing one operator's own platforms would destroy the platform-level detail
their feed deliberately provides. Reconciliation exists to unify *across* operators, not within one.

Candidates are then found via a **~0.2° (≈20 km) spatial grid** rather than scanning all stations.

`FindBestMatch` requires **all four** of:

| Filter | Rule |
|---|---|
| Route type | Must be identical |
| Distance | Within the search radius |
| Name similarity | ≥ 0.3 (a floor to discard noise) |
| **Route overlap** | The stops must share at least one line |
| **Direction** | No conflicting direction on a shared line |

The last two are what make this better than geometry-plus-string matching. Two bus stops on opposite
sides of a road are ~15 m apart with identical names — indistinguishable by name and distance, but
they serve **opposite directions of the same line**, and `HasDirectionMismatch` separates them. Two
unrelated stops that merely share a name are separated by `HasRouteOverlap` finding no common line.

The best candidate is the one with the highest **name score** — not the closest. Distance is a filter,
not the ranking, because coordinates disagree between operators far more than names do.

### Name similarity

```csharp
if (n1 == n2) return 1.0;
if (n1.Length >= 5 && n2.Length >= 5 && (n1.Contains(n2) || n2.Contains(n1))) return 0.85;
return 1.0 - (double)LevenshteinDistance(n1, n2) / Math.Max(n1.Length, n2.Length);
```

Normalised Levenshtein, with a **containment shortcut**: `"Zagreb Glavni kolodvor"` contains
`"Glavni kolodvor"`, and raw edit distance would score that pair poorly despite being obviously the
same place. The ≥5-character guard stops short fragments from matching everything.

`NormalizeName` mirrors `ToNameSlug` — same diacritic folding, same abbreviation expansion — but
preserves spaces, because edit distance over words behaves better than over a concatenated slug. The
abbreviation regexes use `(?<!\w)kol\.(?=\s|$)` so only the standalone abbreviation is expanded, never
a substring of a longer word.

`LevenshteinDistance` is optimised deliberately:

- **Two rolling rows** instead of a full m×n matrix — the comment notes the matrix allocation
  dominated GC, since this runs once per candidate pair across an entire import.
- **`stackalloc`** for strings under 256 characters, avoiding heap allocation entirely.
- **Iterates over the shorter string** so the rows stay small.

### The decision thresholds

| Outcome | Condition | Default |
|---|---|---|
| **Auto-merge** | name ≥ `AutoMergeNameThreshold` **and** distance ≤ `AutoMergeDistanceMeters` | 0.90, 100 m |
| **Manual review** | name ≥ `ManualReviewNameThreshold` **and** distance ≤ `ManualReviewDistanceMeters` | 0.70, 300 m |
| **New station** | otherwise | — |

Both conditions must hold; a perfect name 5 km away is not a match, and neither is a name that merely
rhymes 10 m away.

Crucially, **the thresholds in force are snapshotted onto every candidate**:

```csharp
AutoMergeNameThresholdAtDecision, AutoMergeDistanceMetersAtDecision,
ManualReviewNameThresholdAtDecision, ManualReviewDistanceMetersAtDecision
```

Without this, changing a threshold would silently rewrite the meaning of every historical decision,
and an admin reviewing an old auto-merge could not tell whether it would still be made today. This is
what makes the reconciliation history auditable.

---

## Reconciliation statuses

| Status | Meaning |
|---|---|
| `Pending` | Scored into the review band; awaiting an admin |
| `AutoMerged` | Cleared both auto thresholds; applied without review |
| `ManuallyApproved` | An admin approved it |
| `Rejected` | An admin rejected it |
| `NewStation` | No match; a new canonical station was created |
| `Inactive` | The raw stop is no longer in the active feed version |

`AutoMerged` is kept distinct from `ManuallyApproved` on purpose — `GET /reconciliation/auto-merged`
lets an admin audit what the algorithm did unsupervised, which is the feedback loop for tuning the
thresholds.

---

## Merging and splitting

### Merge

`MergeStationsAsync(sourceId, targetId)`:

1. Move every `RawStop` from source to target.
2. Move `CanonicalStationOperator` links, de-duplicating.
3. **Deactivate** the source (`IsActive = false`) — never delete it.
4. Write a `StationMergeLog` with the source's OnestopId and every moved raw stop id.

Deactivation rather than deletion is what makes the operation **reversible**, and it protects external
references — GetThereAPI stores canonical OnestopIds on imported tickets, and deleting one would
break a ticket already in someone's wallet.

`StationMergeLog` uses `DeleteBehavior.NoAction` on both station FKs precisely because it must
reference a station that has since been deactivated.

`GET /reconciliation/merge-preview` shows what a merge would do before committing, including a
`CanMerge` flag requiring both stations active with the same `PrimaryRouteType`, and a warning string
for the risky cases.

### Unmerge

`Unmerge(mergeLogId)` reads `StationMergeMovedRawStop` — the child table recording exactly which raw
stops moved — and puts each one back, then reactivates the source. The child table exists for this
reason: a merge cannot be reversed from the station rows alone, because after the fact there is no way
to tell which of the target's raw stops came from where.

### Split

There is **no explicit split operation.** `StationSplitLog` records when the *importer* declined to
merge and created a separate station instead, with a reason. It is a diagnostic record of an automatic
decision, not an admin action.

To separate two stations that were wrongly merged, the operations are `unmerge` or `reassign`.

### The deliberate absence of "related but separate"

```csharp
// Decision (Task 4.17): Stations relate to each other in exactly two ways:
//   - Fully merged (source deactivated via IsActive=false, recorded in StationMergeLog)
//   - Fully independent
// No "related but separate" linking concept (e.g. parent/child, related stops)
// exists. SupersedesIds is for legacy ID migration only, not reconciliation merges.
```

This is a real modelling decision with real costs. GTFS itself has `parent_station`, and a large
interchange genuinely is a hierarchy — but supporting that means every query, every merge and every
client has to understand partial relationships. The binary model keeps the invariant simple: a raw
stop belongs to exactly one canonical station, and two canonical stations are either the same place or
not.

`SupersedesIds` on `CanonicalStation`, `CanonicalRoute`, `Operator` and `Feed` exists **only** for
legacy ID migration — an old OnestopId that should still resolve. It is explicitly not a merge record.

---

## Place matching and country detection

`PlaceMatchingManager` attaches each canonical station to a `Place` (a settlement) within
`PlaceMatching:MaxDistanceMeters` (50 km), giving stations a human-meaningful location beyond
coordinates.

Country is derived by `GeoCountryDetector`, a **hand-maintained list of lat/lon bounding boxes,
checked in order**. The ordering is the whole design, and the comments make the intent explicit:

```
Micro-states first        (LI, MC, VA, SM — inside larger countries' boxes)
Italy sub-boxes           (Venezia, Trieste, Udine, Bolzano, Tarvisio, Bologna, Roma)
Austria Carinthia         "checked before SI"
Slovenia sub-boxes        "checked before HR to prevent Slovenian stations falling to HR"
Croatia                   "covers everything SI doesn't claim"
BA, ME, …
```

Boxes overlap, so **first match wins** and specific regions are listed before the broad ones. Adding a
box in the wrong position silently misattributes stations.

This is crude — a rectangle is not a border, and the Croatian box overlaps Bosnia and Hungary. It is
also cheap, dependency-free, offline, and adequate for a Croatia-centred deployment with neighbouring
cross-border feeds. Anything better means shipping real border polygons. When detection fails,
`PlaceMatching:DefaultCountryIsoCode` (default `HR`) applies.

---

## Operational guidance

**Tuning the thresholds.** Raising `AutoMergeNameThreshold` produces fewer wrong merges and more
review work; lowering it does the reverse. A wrong merge is more expensive than a missed one — it
needs an unmerge and may have been served to clients meanwhile — so the defaults lean conservative.
Review `GET /reconciliation/auto-merged` after changing them; the snapshotted thresholds let you tell
old decisions from new.

**Why reconciliation is slow.** It is O(raw stops × nearby stations) with a Levenshtein per pair,
which is why the spatial grid, the rolling-row Levenshtein and the 0.3 name floor all exist. A large
metropolitan feed takes minutes.

**Idempotency.** Re-importing an unchanged feed is cheap: every stop matches on OnestopId in phase 1
and no scoring runs.

**The `Unknown` route type.** `RouteType.Unknown = 999`, and raw stops with a null `RouteType` are
**skipped entirely** by phase 1 — they get no canonical station. A stop served by no trip in the feed
therefore never becomes queryable, which is usually right (it is not in service) but is worth knowing
when a stop is unexpectedly missing.
