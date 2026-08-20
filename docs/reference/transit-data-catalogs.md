# Transit data catalogs & source sites (reference)

Bookmarked sources for finding transit data (GTFS / GTFS-RT / APIs) for Croatian operators — and the
method for cracking a new one. Companion to [`../operator-data-sources.md`](../operator-data-sources.md)
(the per-operator table).

## Catalogs (check in this order)

| Site | URL | What it gives |
|---|---|---|
| **transitous** | https://transitous.org/sources/ · raw feeds: https://raw.githubusercontent.com/public-transport/transitous/main/feeds/hr.json | **Best source.** Curated open GTFS + GTFS-RT per country; ~25 HR operators with working URLs |
| **Mobility Database** | https://mobilitydatabase.org/ | Authoritative global GTFS/GTFS-RT/GBFS catalog with the original producer URLs |
| **busmaps (Croatia)** | https://busmaps.com/en/croatia/feedlist | GTFS mirror (free API); confirms a feed exists + coverage counts |
| **Transitland** | https://www.transit.land/ | Operator/feed registry (feed URLs need an API key now) |

## Feed hosts (where the actual .zip / .pb live)

| Host | URL pattern | Notes |
|---|---|---|
| **vekejsn GitLab generators** | `https://gitlab.com/api/v4/projects/vekejsn%2Fgtfs-generators/packages/generic/<city>-gtfs/latest/<file>.zip` | Public, no auth (verified). Split, Zadar, Libertas, Rijeka/Autotrolej, Sisak, Karlovac |
| **promet-info B2B** | `https://b2b.promet-info.hr/dc/b2b.gtfs.<operator>` | Croatian **National Access Point (NAP)** — official multimodal GTFS (Osijek, Pulapromet, Jadrolinija…). **401 until you register** (see below) |

### promet-info NAP — how to get access (the official route for the 401 feeds)
- **What it is:** Croatia's National Access Point for transport data, run by **Hrvatske ceste d.o.o.**
  under EU Directive 2010/40/EU. `b2b.promet-info.hr/dc/b2b.gtfs.*` are the official GTFS datasets.
- **Access is by free registration.** Per the NAP: *"access to datasets requires prior, free
  registration."* Register as a data user → get credentials → the `401` on the b2b feeds goes away.
- **Register / info:** portal `https://www.promet-info.hr/` (login/registration) · NAP info page
  `https://hrvatske-ceste.hr/hr/stranice/promet-i-sigurnost/dokumenti/76-nacionalna-pristupna-tocka` ·
  contact **javnost@hrvatske-ceste.hr**, +385 1 4722 555.
- **Catalogue** (`promet-info.hr/hr/datasets`, confirmed): official **GTFS** (+ NeTEx) for
  **ZET, HŽ (HŽPP), Autotrolej, GPP Osijek, Pulapromet, Jadrolinija, Karlovac**. Download slugs follow
  `b2b.promet-info.hr/dc/b2b.gtfs.<slug>` (e.g. `osijekgpp`, `pulapromet`, `jl`); grab the exact
  distribution URL from each dataset page when logged in. Auth = **HTTP Basic** (your NAP user/pass).
  Also publishes a **multimodal travel-planner OpenAPI**.
- **Not on the NAP** (keep the community GitLab mirror): Promet Split, Liburnija Zadar,
  Libertas Dubrovnik, AP Sisak, and the small rehosted operators.
| **pirnet GTFS-RT** | `https://rt.gtfs.baguette.pirnet.si/gtfs-rt/<Op>/trip_updates.pb` · `https://rt-misc.ojpp-http.pirnet.si/<op>/trip-updates.pb` | Hosted GTFS-RT (trip updates) for many HR operators |
| **gtfs-rehost** | `https://hoermalmeister.github.io/gtfs-rehost/<city>/<city>.zip` | Community rehost of small-operator GTFS (Crikvenica, Opatija, Poreč, Vela Luka, Rab, Slavonski Brod) |

## Unofficial live maps / dev ecosystems (reverse these for backends)

| Site | Operator(s) | Notes |
|---|---|---|
| https://fleet.promet-split.hr | Promet Split | Official live map; backend `api.promet-split.hr` (GTFS + GTFS-RT, **token-gated**) |
| https://api.split.prometko.si | Promet Split | RE'd proxy by *vekejsn* (`/stops`,`/vehicles`); same author as the GitLab generators |
| https://libertas.enum.hr | Libertas Dubrovnik | App landing page; data in the native app (MITM to get the API) |
| https://zet-uzivo.com | ZET | Unofficial ZET live tracking |
| https://gpp.osijek.digital | GPP Osijek | Community tram live tracker (PULS) |

## Official operator endpoints worth remembering

- **ZET** — GTFS `https://zet.hr/gtfs-scheduled/latest`; GTFS-RT `https://zet.hr/gtfs-rt-protobuf`
- **Autotrolej** — official JSON API (no token): `https://api.autotrolej.hr/api/open/v1/voznired/{stanice,linije,polasci,autobusi}`; live GPS `http://e-usluge2.rijeka.hr/OpenData/AtPoz.php?type=json`
- **HŽPP** — GTFS `http://www.hzpp.hr/Media/Default/GTFS/GTFS_files.zip` (flaky) / data.gov.hr mirror `vozni-red-h-putni-kog-prijevoza-u-gtfs-obliku`
- **AIS (ferries)** — MarineTraffic / VesselFinder / AISHub (by vessel name/MMSI)

## Method for a new/unknown operator
1. Search **transitous `hr.json`** → **mobilitydatabase** → **busmaps**.
2. If none: does **Moovit or Google Maps** show it live? → a GTFS feed exists somewhere; keep looking.
3. Still none: **reverse the operator's live-map web app** — open it, read its JS bundle, grep for
   `/api/…`, `/gtfs…`, `wss://`, `socket`. The app's own backend is the operator's real data source.
4. Only real timetable is PDF/HTML → ingest as a custom source; the schedule carries stop names, fill
   any missing coordinates from OSM.
