# OpenTripPlannerAPI — Detailed Code Documentation

## 1. Purpose of this project
`OpenTripPlannerAPI` is a dedicated scraper/realtime feed host that:
- Pulls transit realtime updates from provider sources (currently HZPP scraper stack),
- Builds GTFS-Realtime protobuf feeds,
- Serves feeds to OTP-compatible consumers,
- Generates OTP config files from `GetThereAPI` operator feed metadata,
- Optionally auto-starts OTP after first successful scrape cycle.

---

## 2. Core architecture

### Startup and hosting
- File: `OpenTripPlannerAPI/Program.cs`
- Hosted as ASP.NET Core service on port `5000`.
- Registers:
  - scraper services (`IScraper`, HZPP components),
  - feed state services (`GtfsFeedStore`, `GtfsReadySignal`, `ProtobufFeedBuilder`),
  - OTP config loader (`DbBackedOtpConfigLoader`),
  - background scraping worker (`ScraperWorker`).

### Feed-serving API
- File: `Controllers/RealtimeController.cs`
- Endpoints:
  - `GET /rt/{feedId}`
  - `GET /{feedId}-rt` (compatibility route)
  - `GET /hzpp-rt` (legacy shortcut)
  - `GET /status` (per-feed freshness/progress summary)

### Background scraping
- File: `Workers/ScraperWorker.cs`
- Loop behavior:
  - initialize enabled scrapers,
  - scrape each feed each interval (`Scrape:IntervalSeconds`),
  - update in-memory feed snapshots and progress counters,
  - signal readiness after first cycle completion.

---

## 3. OTP configuration generation

### Source of truth
- `DbBackedOtpConfigLoader` fetches operator feed metadata from `GetThereAPI` (`/operator/otp-feeds`).

### Output
- Generates/updates `build-config.json` and `router-config.json` used by OTP runtime.

### Resilience behavior
- Includes fallback behavior to existing local config files when operator-source fetch fails.
- Normalizes fallback realtime URLs when needed (loopback/host resolution scenarios).

---

## 4. Current scraper stack

### HZPP scraper
- Files under `OpenTripPlannerAPI/Scrapers/Hzpp`
- Includes GTFS loader + scrape logic + domain models.
- Produces stop-time update payloads converted into GTFS-RT protobuf.

### Feed storage and serialization
- `GtfsFeedStore` stores latest bytes per feed.
- `ProtobufFeedBuilder` creates payloads (including empty feed payloads for safe fallback).

---

## 5. How future code for this project should be done

### Add new feed providers through scraper abstraction
1. Implement new scraper class via `IScraper`/`ScraperBase`.
2. Assign unique `FeedId`.
3. Register scraper in DI.
4. Reuse common protobuf/feed store components.

### Keep feed API stable
- Preserve `/rt/{feedId}` as canonical endpoint.
- Keep compatibility aliases where practical to avoid downstream breakage.

### Keep config generation centralized
- Continue deriving OTP feed config from `GetThereAPI` operator data.
- Avoid hardcoding long-term feed lists in static files.

### Reliability and operations rules
- Never block app startup waiting for endless scrape loops.
- Preserve first-cycle readiness signaling (critical for OTP start sequencing).
- Keep per-feed progress metrics exposed through `/status`.

---

## 6. Recommended next improvements
- Add per-scraper health diagnostics endpoint with last error and last success timestamps.
- Add retry/backoff policy for upstream provider outages.
- Add structured logs with feed id correlation keys.
- Add integration tests for config generation and fallback behavior.
