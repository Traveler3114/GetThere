# OpenTripPlannerAPI — Full Code Documentation

## 1) Project role in the solution
`OpenTripPlannerAPI` is a specialized scraper + GTFS-Realtime feed host.

It bridges external realtime source data (currently HZPP scraping flow) into OTP-consumable GTFS-RT protobuf endpoints and prepares OTP runtime configs from SQL Server operator metadata.

---

## 2) Runtime/platform basics
- Project file: `OpenTripPlannerAPI/OpenTripPlannerAPI.csproj`
- SDK: `Microsoft.NET.Sdk.Web`
- Target framework: `net10.0`
- Main packages:
  - `Google.Protobuf`
  - `CsvHelper`

Host URL is set in startup to `http://0.0.0.0:5000`.

---

## 3) Startup and service graph (`Program.cs`)

### Registered named HTTP clients
- `gtfs` (60s timeout)
- `hzpp` (base address `https://www.hzpp.app`, custom headers, 15s timeout)

### Registered DB services
- `OtpReadDbContext` (read-only SQL Server context)

### Registered singleton services
- `GtfsFeedStore`
- `GtfsReadySignal`
- `ProtobufFeedBuilder`
- `DbBackedOtpConfigState`
- `DbBackedOtpConfigLoader`
- `HzppGtfsLoader`
- `IScraper -> HzppScraper`

### Hosted services
- `ScraperWorker`

### Startup sequence
1. Build app and map controllers.
2. Load/generate OTP config via `DbBackedOtpConfigLoader.LoadAndGenerateAsync()` from SQL Server (DB-only).
3. Start app host.
4. Wait for first scrape cycle readiness (`GtfsReadySignal`).
5. Optionally auto-start OTP Java process in separate terminal based on config.
6. Wait for shutdown.

---

## 4) Configuration (`appsettings.json`)

### Gtfs
- `ZipUrl` fallback for HZPP static GTFS zip.

### Scrape
- `IntervalSeconds`
- `RequestDelaySeconds`

### Otp auto-start
- `AutoStart`
- `JavaExecutable`
- `JarPath`
- `Arguments`
- `WorkingDirectory`

### OperatorSource
- `UpdaterFrequency`
- `HzppFallbackRealtimeUrl`
- `TransitModelTimeZone`
- `StrictReachabilityChecks`

---

## 5) Exposed HTTP endpoints
Controller: `Controllers/RealtimeController.cs`

### Realtime feed endpoints
- `GET /rt/{feedId}` (canonical)
- `GET /{feedId}-rt` (compat route)

Behavior:
- Reads latest bytes from in-memory feed store.
- If feed missing/empty -> serves an empty valid GTFS-RT feed.
- Content type: `application/x-protobuf`.

### Status endpoint
- `GET /status`
- HTML text output with per-feed:
  - size,
  - age,
  - processed/total progress,
  - updates count.

---

## 6) Core internal components

### GtfsFeedStore (`Core/GtfsFeedStore.cs`)
In-memory concurrent dictionary keyed by `feedId` storing:
- feed bytes
- last update timestamp
- progress metrics

### ProtobufFeedBuilder (`Core/ProtobufFeedBuilder.cs`)
Builds GTFS-RT `FeedMessage` from stop-time updates map:
- one `FeedEntity` per trip id,
- includes delay fields in arrival/departure events,
- can generate empty dataset feed.

### GtfsReadySignal (`Core/GtfsReadySignal.cs`)
Task completion signal used to coordinate post-first-scrape startup actions.

---

## 7) Scraper architecture

### Base contracts
- `IScraper`:
  - `FeedId`
  - `IsEnabled`
  - `InitialiseAsync`
  - `ScrapeAsync`

- `ScraperBase`:
  - helper methods for producing `ScrapeResult` with protobuf bytes.

- `ScrapeResult`:
  - bytes + processed/total/with-updates metrics.

### Worker loop (`ScraperWorker`)
- Filters enabled scrapers.
- Initializes each scraper once.
- On each interval:
  - runs scrape,
  - updates feed store,
  - logs errors per scraper without crashing loop.
- Sets ready signal after first full cycle.

---

## 8) HZPP scraper implementation details
Files under `Scrapers/Hzpp`.

### Enablement logic
`HzppScraper.IsEnabled` depends on `DbBackedOtpConfigState.UsesLocalHzppScraper`, meaning scraper activation is driven by DB-derived OTP config and updater URL matching.

### Initialization
- Loads static GTFS via `HzppGtfsLoader.LoadAsync`.
- Uses local HZPP static GTFS URL from config state if available, else fallback config value.

### Scrape cycle flow
1. Determine active train numbers from GTFS calendar/trips.
2. For each train:
   - fetch HZPP train data endpoint,
   - parse HTML-like payload from chunked JSON lines,
   - compute delay/current station/finished flags,
   - resolve active trip id,
   - compute stop-time updates.
3. Build GTFS-RT bytes from aggregated updates.
4. Return progress metrics.

### Parsing behavior
Regex-based extraction for:
- station,
- route text,
- delay minutes,
- finished trip marker.

### Update computation logic
- Matches current station against GTFS stop names (normalized).
- Skips already-passed stops unless trip finished logic dictates otherwise.
- Applies delay seconds to scheduled arrival/departure times.

---

## 9) GTFS static loader details (`HzppGtfsLoader`)

### Reads files from GTFS zip
- `stops.txt`
- `trips.txt`
- `stop_times.txt`
- `calendar.txt` (if missing, all trips treated active)

### Data structures produced
- `StopsById`
- `StopIdByName`
- `TripsById`
- `TripsByTrain`
- `StopTimes` by trip
- `Calendar` service-date sets

### Utility behavior
- Time parsing handles `HH:mm[:ss]`, supports over-24-hour GTFS values by numeric conversion.
- Active train and active trip helper methods use Zagreb timezone date context.

---

## 10) DB-backed OTP config generation (`DbBackedOtpConfigLoader`)

### Source contracts
- DB source: reads operator feeds directly from SQL Server (read-only context).

### Generated files
- `build-config.json` with `transitFeeds` list.
- `router-config.json` with STOP_TIME_UPDATER list.

### Validation and safety
- Validates feed ids and URL syntax.
- Optional strict reachability checks using HEAD requests.
- Detects whether local HZPP scraper should be active by comparing realtime updater URL with configured fallback URL.

### Fallback mode
If selected source fetch/read fails:
- attempts to use existing local config files,
- derives scraper state from existing router/build configs,
- logs warning with fallback path context,
- throws only if no usable fallback config exists.

---

## 11) OTP auto-start behavior
After first scrape readiness, startup can spawn OTP in a new terminal window.

Platform-specific launch paths:
- Windows: `cmd /c start ...`
- macOS: AppleScript -> Terminal
- Linux: tries common terminal executables (`x-terminal-emulator`, `gnome-terminal`, etc.)

If launch fails, app logs warning and continues running.

---

## 12) Rules for future development in this project

### Rule A — add providers through `IScraper`
New feed support should be implemented as separate scraper classes with unique `FeedId`, then registered in DI.

### Rule B — keep feed endpoint compatibility
Maintain `/rt/{feedId}` as canonical and preserve compatibility aliases where possible.

### Rule C — keep config DB-driven
Do not hardcode long-term feed lists manually when DB-backed source exists.

### Rule D — preserve observability
Continue exposing per-feed progress and freshness metrics.

### Rule E — graceful degradation
If upstream source fails, serve empty valid GTFS-RT rather than malformed/failed payload.

---

## 13) Recommended future improvements
- Per-scraper health endpoint with last-success/last-error metadata.
- Retry/backoff policies for flaky upstreams.
- Optional persistence of recent feed snapshots for diagnostics.
- Automated tests for config generation, fallback behavior, and scraper update transformation.
