# TransitInfoAPI — Zagreb Transit Coverage

The routing engine (see ROADMAP Phase 3) plans journeys over an OTP graph that is built **only** from
what TransitInfoAPI exports — the reconciled canonical network plus an OSM extract. It never fetches
operator feeds itself. So "what can Zagreb route over?" is answered entirely by "what feeds and custom
sources does TransitInfoAPI hold, in scope?" This document is that inventory, plus the two gaps that
need hand-built sources and the licence terms each feed carries.

This is a **snapshot for planning**, not a runtime source of truth. The exporter is a query over the
canonical model (active `FeedVersion` rows, in-scope stops, `MobilityStation`) — it never reads this
file and never enumerates operators. Adding an operator is inserting rows; it is not editing this doc.

---

## What is on disk today

Feed archives live under `TransitInfoAPI/feeds/{feedId}/`, resolved by
`Services.FeedStorage.DirectoryFor(feedId)` (path-containment checked). As of this writing the
directory holds:

| Dir | Agency | Scope | Contents | Zagreb-relevant |
|---|---|---|---|---|
| `zet/` | Zagrebački Električni Tramvaj | Zagreb city | tram + bus, shapes; service via `calendar_dates` | **Yes — the core feed** |
| `hžpp/` | HŽ Putnički prijevoz | National rail | rail; no `calendar_dates.txt`, no `shapes.txt` | **Yes — suburban + intercity rail** |
| `obb/` | ÖBB | Austria | has `pathways.txt` + `levels.txt` | No — out of Zagreb scope |
| `gp/` | Gradski parking d.o.o. | Šibenik (phone +385 22) | minimal | No |
| `custom-5-autotrolejstatic/` | Autotrolej | Rijeka | static GTFS custom source | No — Rijeka, not Zagreb |
| `autotrolej-api/` | Autotrolej | Rijeka | `custom-source.json` (HTTP) | No — Rijeka |
| `upload-pdf-test/` | — | test fixture | `custom-source.json` (PDF upload) | No — test only |

> **Correction to an earlier audit.** There is **no `fb/` (FlixBus) archive on disk.** Any prior table
> listing FlixBus as "have it on disk" is wrong. If long-distance coach coverage is wanted it is a new
> feed to add, not something already present.

So the Zagreb-relevant static feeds on disk today are exactly **ZET** (core tram + bus) and **HŽPP**
(rail). Nextbike is ingested separately as live GBFS via `MobilityManager` (not a GTFS archive).

---

## Finding 1 — ZET expresses all service through `calendar_dates`, with a cosmetic `end_date`

ZET's `calendar.txt` sets **all seven weekday flags to 0** on every service; real service is expressed
entirely as `calendar_dates.txt` exceptions. The `end_date=20301231` in `calendar.txt` and
`feed_info.txt` is cosmetic — it describes no service. The **actual** service window is whatever the
`calendar_dates` exceptions span.

**Current status (verified against the on-disk archive):** the exceptions run **20260807 → 20261231**,
i.e. service is defined through **31 Dec 2026**. An earlier audit recorded 20260706→20260906 (expiring
2026-09-06); the archive has since been refreshed, so the near-term "graph goes empty in ~3 weeks"
emergency described there **no longer applies to this archive**.

**What still stands:** because the real window lives only in `calendar_dates` and the `end_date` lies,
an OTP graph will silently return **zero itineraries** once the exceptions lapse, with nothing in the
feed metadata to warn of it. Therefore:

- Automated feed refresh must stay running (`FeedPollingWorker` → `FeedManager.CheckAndFetchAsync`).
- **OTP graph rebuild must be tied to feed activation** (Step 4), so a refreshed ZET feed rebuilds the
  graph rather than requiring a manual step. Wire this in from the start — do not retrofit.

A cheap ongoing health check: alert when the max `calendar_dates` date on the active ZET version is
within, say, 21 days of now.

---

## Finding 2 — the Uspinjača funicular and Sljeme cable car are in no feed

ZET's `routes.txt` contains only `route_type` 0 (tram) and 3 (bus). The **Uspinjača funicular**
(`route_type` 7) and the **Sljeme cable car** (`route_type` 6, gondola) are ZET-operated, carry
passengers, and appear in no GTFS feed anywhere. Both are fixed, tiny, and trivially schedulable →
hand-built `CustomSource` rows (Step 3). The **Zagreb Airport shuttle** (Pleso prijevoz, PDF timetable)
is the third hand-build candidate.

---

## Operator coverage

Feeds come from the operators themselves — the only source this plan uses, at audit time and at
runtime. No third-party feed catalogue is involved.

| Operator | Mode | GTFS static | Source | GTFS-RT | Verdict |
|---|---|---|---|---|---|
| ZET | tram, bus | ✅ on disk | zet.hr | ✅ `https://zet.hr/gtfs-rt-protobuf` (trip updates) | Have it — keep refresh + rebuild-on-activation |
| HŽPP | rail | ✅ on disk | operator | ❌ none published | Have it — no real-time (scheduled times only) |
| Nextbike Zagreb | bike share | n/a (GBFS) | operator GBFS | ✅ live availability | Already ingested via `MobilityManager` |
| ZET Uspinjača (funicular) | funicular | ❌ | published timetable | ❌ | **Hand-build** (Step 3) |
| ZET Sljeme cable car | gondola | ❌ | published timetable | ❌ | **Hand-build** (Step 3) |
| Zagreb Airport shuttle (Pleso prijevoz) | bus | ❌ | PDF timetable | ❌ | **Hand-build** (Step 3) |
| Zagreb County suburban coaches (Samoborček / Presečki / Čazmatrans / Meteor) | bus | ❌ | PDF timetables | ❌ | Out of launch scope — revisit after |
| Arriva Hrvatska | coach | ❌ | — | ❌ | Out of launch scope |

The three hand-build rows are the actionable Step 3 work. The PDF-timetable rows are the "Case B" shape
(stop names + times, no ids, no coordinates) described in the plan.

---

## Licence terms to record on each feed

`Feed` already carries `LicenseName`, `LicenseUrl`, `LicenseRedistributionAllowed` (plus
`LicenseCommercialUseAllowed`, `LicenseShareAlikeOptional`). ROADMAP Phase 7 requires enforcing these
before TransitInfoAPI goes public, so they must be populated per feed. Feeds are created at runtime via
the admin console / `FeedManager` (not seeded in code), so these are **admin/data actions**, set on the
feed row.

| Feed | `LicenseName` | `LicenseUrl` | `LicenseRedistributionAllowed` |
|---|---|---|---|
| ZET | `Open licence — Republic of Croatia` | `http://www.zet.hr/odredbe/datoteke-u-gtfs-formatu/669` | `true` |

> ZET attribution is **mandatory**. Required text: *"Public dataset by ZET provided under Open license,
> dataset source http://www.zet.hr/odredbe/datoteke-u-gtfs-formatu/669"*.

Record HŽPP's and any future feed's terms the same way as they are confirmed with each operator; leave
`LicenseRedistributionAllowed` null (unknown) rather than guessing until confirmed.

---

## Which location source does what (read before Steps 2, 4, 5)

Three different things deal with locations. They do not overlap:

| Source | For | Not for |
|---|---|---|
| **Geofabrik OSM extract** (Step 2) | (1) the street/footpath network OTP routes over; (2) a stop **corpus** (`highway=bus_stop`, `railway=tram_stop`, `public_transport=platform`) for the Case B name-match fallback | Not for geocoding street addresses. Not for overriding a feed that publishes its own stops. |
| **Azure Maps Search** (Step 5) | street address → coordinates, for the trip's start and end | Not a stop lookup — stop names geocode badly against an address DB. |
| **`CanonicalStation` / `RawStop`** in the DB (Step 4) | where stops are, for every feed that publishes them — already reconciled and coordinated. The default; covers every feed on disk today. | Not derived from OSM, except the Case B fallback. |

**Case A vs Case B** — when an operator publishes a schedule but no stops:

- **Case A** (the common case, all current feeds): the stop ids already resolve to a
  `CanonicalStation` another feed defined. Resolution is the exact FK `StopTime.RawStopEntityId` /
  `CanonicalStationId` (backfilled at import) — never an OSM proximity guess. The Step 4 exporter
  handles this.
- **Case B** (a PDF timetable — names + times, no ids): mint stop ids deterministically from the name
  (`MappingKind.Expression`), resolve coordinates against OSM by **name + route sequence** (not each
  name alone), through the `ReconciliationCandidate` review queue. No feed is in Case B today; build it
  only when a source needs it.

**On `gtfs:stop_id`:** ZET's GTFS stops were imported into OSM (~2020-12-05) with `gtfs:stop_id` +
`official_name` tags. That makes an exact ZET id-join *possible* against OSM — but ZET is **not** a
Case B operator (its stops are already in the DB with coordinates), so this is **not** the Case B
mechanism. It is worth **measuring** in Step 2 (count OSM nodes carrying `gtfs:stop_id`, join against
ZET's current `stops.txt`) purely as a **freshness probe** on OSM's ~6-year-old Zagreb stop data. A low
hit rate means OSM names have drifted and Case B name-matching is a review-queue problem, not a cheap
join. Record that number in the Step 4 export verification.
