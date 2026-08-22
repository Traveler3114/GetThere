# Croatian Transit Operators — Data Sources

Every Croatian operator we care about, the best data we can ingest, official + unofficial. Verified
live where marked ⚡. Corrects the original CSV (which had several "no GTFS" errors).

## TL;DR
**`transitous.org` is the master source.** It curates open GTFS + GTFS-RT for ~25 Croatian operators,
most via the public **`vekejsn/gtfs-generators`** GitLab (no auth, verified downloading). Start there;
fall back to official operator feeds / APIs; use OSM only to fill missing stop coordinates.

---

## Catalogs & reference sites (save — reuse these first for any new operator)

| Site | What it gives | Notes |
|---|---|---|
| **transitous.org/sources** + raw `feeds/hr.json` | Curated open GTFS + GTFS-RT per country | Best single source. Raw: `raw.githubusercontent.com/public-transport/transitous/main/feeds/hr.json` |
| **mobilitydatabase.org** | Authoritative global GTFS/RT catalog w/ original producer URLs | The canonical lookup for a feed's official URL |
| **busmaps.com/en/croatia/feedlist** | GTFS mirror (free API) | Confirms a feed exists + coverage counts |
| **gitlab.com/vekejsn/gtfs-generators** | Public GTFS zips (Split, Zadar, Libertas, Rijeka, Sisak, Karlovac…) ⚡ | Community-generated but public + used by transitous. `…/packages/generic/<city>-gtfs/latest/<file>.zip` |
| **b2b.promet-info.hr** | Croatian national B2B GTFS (Osijek, Pulapromet, Jadrolinija…) | **Token-gated (HTTP 401)** — needs a promet-info B2B key |
| **pirnet.si** (`rt.gtfs.baguette.pirnet.si`, `rt-misc.ojpp-http.pirnet.si`) | GTFS-RT hosting for HR operators | trip-updates `.pb` per operator |
| **hoermalmeister.github.io/gtfs-rehost** | Rehosted GTFS for small operators (Crikvenica, Opatija, Poreč, Vela Luka, Rab, Slavonski Brod) | Community rehost |
| **prometko.si** (`api.split.prometko.si`, dev *vekejsn*) | RE'd Split backend (`/stops`,`/vehicles`) | Same author as the GitLab generators |
| **enum.hr** (`libertas.enum.hr`) | Libertas Dubrovnik app | Native app; landing page only on web |
| **zet-uzivo.com**, **gpp.osijek.digital** | Unofficial live maps (ZET, Osijek tram) | Reverse for backend if needed |

**Method for a new operator:** check transitous `hr.json` → mobilitydatabase → busmaps → else reverse
the operator's live-map web app (open it, read its JS, grep `/api`, `/gtfs`, `wss://`). **Signal:** if
Moovit/Google Maps show the operator live, a GTFS feed exists somewhere.

---

## Master table — ALL Croatian operators

Legend: ⚡ verified downloading; 🔒 token-gated; — none/unknown. GTFS-RT column = trip-updates unless noted.

| # | Operator | Region | GTFS (static) | GTFS-RT | Other / unofficial | Ingest as |
|---|---|---|---|---|---|---|
| 1 | **ZET** | Zagreb | `zet.hr/gtfs-scheduled/latest` ⚡ | `zet.hr/gtfs-rt-protobuf` (test-labelled) | zet-uzivo.com | Feed (live) |
| 2 | **HŽPP** (rail) | national | `hzpp.hr/api/v1/repository/download/852f76d2-8df3-4957-b30b-dc4b88b1caca` (transitous) | pirnet `https://rt.gtfs.baguette.pirnet.si/gtfs-rt/HZPP/trip_updates.pb` | data.gov.hr mirror | Feed (live) |
| 3 | **Promet Split** | Split | `gitlab.com/…/vekejsn/gtfs-generators/…/split-gtfs/latest/split_gtfs.zip` ⚡ (20.7 MB) | pirnet `…/prometSplit/trip_updates.pb` | official `api.promet-split.hr` 🔒 (401); `api.split.prometko.si` | Feed (GTFS + RT) |
| 4 | **Autotrolej** | Rijeka | `gitlab.com/…/rijeka-gtfs/…/autotrolej_gtfs.zip` | pirnet `…/autotrolej/trip-updates.pb` | official API `api.autotrolej.hr/api/open/v1/voznired/*` ⚡ + live GPS | Feed (GTFS) **or** JSON custom source |
| 5 | **Liburnija Zadar** | Zadar | `gitlab.com/…/zadar-gtfs/…/zadar_gtfs.zip` ⚡ (8.5 MB) | pirnet `…/liburnijaZadar/trip_updates.pb` | HTML `liburnija-zadar.hr/red-voznje/` | Feed |
| 6 | **Libertas Dubrovnik** | Dubrovnik | `gitlab.com/…/libertas-gtfs/…/libertas_gtfs.zip` ⚡ (3.3 MB) | — | app `libertas.enum.hr`; Moovit | Feed |
| 7 | **GPP Osijek** | Osijek | **official NAP** `b2b.promet-info.hr/dc/b2b.gtfs.osijekgpp` (Basic auth ✓ registered) | — | busmaps mirror (140 routes/316 stops→2028); tram `gpp.osijek.digital` | Feed (Official) |
| 8 | **Pulapromet** | Pula | **official NAP** `b2b.promet-info.hr/dc/b2b.gtfs.pulapromet` (Basic auth ✓ registered) | pirnet `…/pulapromet/trip-updates.pb` | official PDFs; app "Mobility Plus" | Feed (Official) |
| 9 | **Gradski Parking Šibenik** | Šibenik | `gradski-parking.hr/upload/stranice/2022/08/2022-08-30/89/gtfs.zip` ⚡ | pirnet `…/sibenik/trip-updates.pb` | busmaps mirror | Feed |
| 10 | **Jadrolinija** (ferry) | national | **official NAP** `b2b.promet-info.hr/dc/b2b.gtfs.jl` (Basic auth ✓ registered) | — | busmaps mirror; `jadrolinija.hr/en/travels` JSON; AIS | Feed (Official) |
| 11 | **AP Sisak** | Sisak | `gitlab.com/…/sisak-gtfs/…/ap_sisak_gtfs.zip` | pirnet `…/apSisak/trip_updates.pb` | — | Feed *(new — not in CSV)* |
| 12 | **Autotransport Karlovac** (Grad Karlovac) | Karlovac | **official NAP** `b2b.promet-info.hr/dc/b2b.gtfs.karlovac` (Basic auth); fallback `gitlab.com/…/karlovac_gtfs.zip` | — | part of Čazmatrans group | Feed (Official) |
| 13 | **Crikvenica** | Kvarner | `hoermalmeister.github.io/gtfs-rehost/crikvenica/crikvenica.zip` | — | — | Feed *(new)* |
| 14 | **Opatija** | Kvarner | `hoermalmeister.github.io/gtfs-rehost/opatija/opatija.zip` | — | — | Feed *(new)* |
| 15 | **Poreč** | Istria | `hoermalmeister.github.io/gtfs-rehost/porec/porec.zip` | — | — | Feed *(new)* |
| 16 | **Sveta Nedelja** | Zagreb Co. | `hoermalmeister.github.io/gtfs-rehost/sveta_nedelja/sveta_nedelja.zip` | — | — | Feed *(new)* |
| 17 | **Vela Luka** (Korčula) | Dubrovnik-Neretva | `hoermalmeister.github.io/gtfs-rehost/vela_luka/vela_luka.zip` | — | — | Feed *(new)* |
| 18 | **Terzić** (Slavonski Brod) | Brod-Posavina | `hoermalmeister.github.io/gtfs-rehost/slavonski_brod/slavonski_brod.zip` | — | — | Feed *(resolves the CSV "Brod-Posavina placeholder")* |
| 19 | **Rapska plovidba** (ferry) | Rab | `hoermalmeister.github.io/gtfs-rehost/rapska_plovidba/rapska_plovidba.zip` | — | — | Feed *(new)* |
| 20 | **Rapska vozidba** | Rab | `owncloud.cesnet.cz/index.php/s/UudJhpom7fgur2X/download` | — | — | Feed *(new)* |
| 21 | **Arriva / Autotrans** (Kvarner+Istra+Hrvatska = 1) | multi | — | — | search-UI only `arriva.com.hr/en-us/route-map` | Defer / OSM stops only |
| 22 | **Čazmatrans** (group) | multi | — (but Karlovac/Sisak sub-cos have GTFS, #11/#12) | — | PDFs `cazmatrans.hr/documents/*.pdf` | PDF custom source + matcher |
| 23 | **Krilo (Kapetan Luka)** (ferry) | Split | — | — | HTML `krilo.hr/en/sailing-schedule/`; AIS | HTML custom source |
| 24 | **TP Line** (ferry) | Split | — | — | HTML `tp-line.hr/en/page/timetable`; per-vessel AIS | HTML custom source |
| 25 | **G&V Line** (ferry) | Zadar/Dubrovnik | — | — | minimal web; ferrycroatia.com; AIS | Defer |
| 26 | **Bura Line** (catamaran) | Split | — | — | ferry directories; AIS | Defer |
| 27 | **Polet Vinkovci** | intercity | — | — | PDF/HTML own-site or `autobusni-kolodvor.com` | PDF/HTML custom source (low prio) |
| 28 | **Slavonija Bus** | intercity (Osijek) | — | — | aggregator PDF/HTML | Low prio |
| 29 | **Panturist** | intercity (Osijek) | — | — | aggregator PDF/HTML | Low prio |
| 30 | **Samoborček** | intercity (Samobor) | — | — | aggregator PDF/HTML | Low prio |
| 31 | **Vincek** | intercity (Zagorje) | — | — | aggregator PDF/HTML | Low prio |
| 32 | **Presečki Grupa** | intercity (Krapina) | — | — | aggregator PDF/HTML | Low prio |
| 33 | **AP Varaždin** | Varaždin | — | — | aggregator PDF/HTML (dup CSV row = same op) | Low prio |
| 34 | **Brioni Pula** | intercity (Pula) | — | — | directories; Nomago partnership | Low prio |
| 35 | **FlixBus** | pan-EU | unofficial Transitland mirror `transit.land/feeds/f-u-flixbus` | — | WIMB `global.api.flixbus.com/gis/v2/timetable/{station}/departures?from={now}&to={now+90m}&apiKey=…` (JSON) — `flixbus.com/robots.txt` disallows `/track/station/` | Custom source (ships **disabled**, `ReverseEngineered`) |

Notes:
- **Rows 11–20 are NEW** operators/feeds transitous surfaced that the original CSV never listed — free extra coverage.
- **`Brod-Posavina transit`** in the CSV was a placeholder → it's **Terzić** (#18), which has GTFS.
- **`AP Varaždin` == `Autobusni prijevoz Varaždin`** — one operator (CSV listed twice).
- **`vekejsn` GitLab** feeds are community-generated but **public and used by transitous** — reliable enough to ingest; flag provenance as unofficial-but-open.
- **`b2b.promet-info.hr`** is the **official Croatian NAP** (Hrvatske ceste). Its catalogue
  (`promet-info.hr/hr/datasets`) has official GTFS for **ZET, HŽ, Autotrolej, GPP Osijek, Pulapromet,
  Jadrolinija, Karlovac**. Auth = **HTTP Basic** (free NAP registration — done). So Osijek/Pulapromet/
  Jadrolinija/Karlovac are now **Official + activatable** with the NAP login (see
  `reference/transit-data-catalogs.md`). Split/Zadar/Libertas/Sisak are **not** on the NAP → keep the
  community GitLab mirror.

---

## Realtime summary
- **GTFS-RT (pirnet-hosted `.pb`):** HŽPP, Promet Split, Autotrolej, Liburnija Zadar, Pulapromet, Šibenik, AP Sisak.
- **GTFS-RT (operator):** ZET (`zet.hr/gtfs-rt-protobuf`).
- **JSON vehicle API:** Autotrolej (`…/voznired/autobusi`, `e-usluge2.rijeka.hr/OpenData/AtPoz.php`).
- **AIS only:** all ferries (Jadrolinija, Krilo, TP Line, G&V, Bura, Rab).
- **App-only (needs MITM):** Libertas, Pulapromet app, Promet Split app.

## Recommended ingestion
1. **Bulk-onboard the GTFS operators as Feeds** from transitous URLs (rows 1–20). Prefer the public
   GitLab (`vekejsn`) ones; for the 🔒 b2b ones (Osijek/Pulapromet/Jadrolinija) get a non-gated URL
   (promet-info key or transitous-served copy).
2. **Attach GTFS-RT** (pirnet `.pb`) where listed.
3. **Autotrolej** — GTFS is fine, or keep the richer official JSON API.
4. **Non-GTFS operators** (Arriva, Čazmatrans PDFs, intercity long tail, non-Jadrolinija ferries) →
   custom sources (HTML/PDF) with the coordinate-matcher filling stop coordinates from OSM. Low priority.

---

## Alert & disruption sources (for the routing-alerts feature)

Separate from *schedules* (above): where each operator publishes **service alerts / disruptions**
(cancellations, line suspensions, temporary reroutes). **Verified live 2026-08-21.**

### Proven findings
1. **No Croatian operator publishes GTFS-RT service alerts.** Decoded `zet.hr/gtfs-rt-protobuf`
   directly: **766 entities = 483 `trip_update` + 283 `vehicle` + 0 `alert`**. pirnet `…/alerts.pb`
   and `…/service_alerts.pb` → **404**; only `trip_updates.pb` exists for every operator.
2. **Cancellations don't arrive via GTFS-RT either** — all 483 ZET trip updates were
   `schedule_relationship = SCHEDULED`, no CANCELED trips, no SKIPPED stop-times. So the
   `stop-time-updater` cannot tell you "this bus isn't running today".
3. **Every operator posts disruptions as HTML notices, with the line number in the title** — which is
   what makes them matchable to routes and therefore usable in routing.

⇒ **HTML scraping is the only channel for operator alerts.** Ingest via the generic alert-source
engine reusing `CustomSourceEngine.ParseHtmlRows` (CSS selectors) → `Alert` rows → match line numbers
to `CanonicalRoute` → synthesize a GTFS-RT alerts feed for OTP.

### Verified sources (all HTTP 200, server-rendered — selectors valid 2026-08-21)

| Operator | URL | Item selector | Sample title seen |
|---|---|---|---|
| **ZET** | `zet.hr/aktualnosti/izmjene-u-prometu/31` | `a[href*="/izmjene-u-prometu/"]` | *"Linije 6, 8 i 14 mijenjaju trase prometovanja"* |
| **HŽPP** | `hzpp.hr/hr/informacije?type=info` ⚠ **not** `/hr/obavijesti` (JS-only, 25 KB) | `div.accordion-item.railway-works-accordion` | *"Radovi na pruzi — Zagreb GK - Dugo Selo … (24.8.–11.9.)"* |
| **Autotrolej** | `autotrolej.hr/obavijesti/` | `div.news-content` (+`.news-meta`) | *"Privremena izmjena trase linije 12A"*, *"Stajalište Novi list B izvan funkcije"* |
| **Promet Split** | `promet-split.hr/obavijesti/category/obavijesti` | `article.c-article-card` (+`__date`,`__summary`) | *"OBAVIJEST ZA PUTNIKE NA LINIJI BR. 33"* |
| **GPP Osijek** | `web.gpp-osijek.com/kategorija/promet/` | `div.entry-main` (+`.entry-date`) | *"Tramvajska linija T1 … voze obilazno zbog radova"* |
| **Pulapromet** | `pulapromet.hr/novosti` | `a[href*="/novosti/detaljnije/"]` | *"Koncert u Amfiteatru – LORDE"* (event reroutes) |
| **Jadrolinija** | `jadrolinija.hr/en/user-notifications` · `…/news-single/stanje-u-pomorskom-prometu` | `article.card` (+`.card__data`) | *"Linije u prekidu"*, cancelled/retimed sailings |
| **HAK** (roads) | NAP `b2b.hak.events.geojson.hr_HR`, `b2b.hak.roadworks.geojson.hr_HR` (Basic auth) | GeoJSON `features` | road events + roadworks → `Kind=Road` |

Notes: HŽPP has a `/api/v1/*` JSON API but it returns **401** (auth-gated) — scrape the info page
instead. Promet Split's `api.promet-split.hr` is likewise token-gated. Selectors **will drift** — the
ingester should log a warning (not throw) when a source yields 0 items. Alert sources are now `Feed`
rows of type `AlertSource` (`AlertSource` table, managed at `/admin/alert-sources.html` and seeded
idempotently), not `appsettings.json` `Alerts:Sources`.
