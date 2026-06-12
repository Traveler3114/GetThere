# GetThere OTP Phase 1 — Architecture Plan & Status

---

## Overview

**Goal:** Implement OpenTripPlanner (OTP) as the single source of truth for transit data:
- Stops
- Schedules
- Routes
- Realtime delays (when available)

The system must be designed so future features can be added without refactoring core architecture:
- Flights
- Multi-region routing
- AI route optimization
- GBFS (bikes/scooters)

**Core Principle:**
> OTP = regional transit engine (not global system)
> GetThere API = global orchestration layer

---

## Architecture Plan

### OTP Role (Phase 1)

OTP is responsible for:
- All transit stops
- All schedules
- All routes
- All rail/bus/tram/metro data
- Realtime delays (GTFS-RT)

Do **not**:
- Parse GTFS in backend
- Use Transitland
- Run vehicle tracking
- Store schedules in DB

OTP is the **only** transit data source.

### Data Flow

```
Frontend
↓
GetThere API (ASP.NET)
↓
OtpClient (GraphQL)
↓
OpenTripPlanner
```

### OTP Setup (Current Scope)

- Single OTP instance (EU)
- Load GTFS feeds (ZET, HZPP, LPP, OBB)
- Enable GTFS-RT where available (delays only)

---

## Backend Responsibilities

### OtpClient (GraphQL only)
- `GetStops(countryId)`
- `GetStopSchedule(stopId, countryId)`
- `GetRoutes(countryId)`
- `HealthCheck()`

### ITransitProvider (abstraction)
- `GetStops(countryId)`
- `GetStopSchedule(stopId, countryId)`
- `GetRoutes(countryId)`
- `HealthCheck()`
- Route planning methods defined but not implemented yet

### OtpTransitProvider
- Implements `ITransitProvider`
- Uses `OtpClient`
- Maps OTP responses to API DTOs

### OperatorManager
- Thin orchestration layer
- No parsing logic
- No data ownership
- Only mapping and coordination

---

## Frontend Behavior

- Load all stops per selected country
- No viewport filtering
- On stop click, fetch schedule
- Display delays if available
- No vehicle tracking

**Caching:**
- Stored on frontend device only
- Backend remains stateless

---

## What Was Removed

- GTFS static parsers
- GTFS-RT parsers
- Transitland integration
- Vehicle tracking system
- Background polling services
- Schedule persistence logic

---

## Current Architecture (Phase 1 Target)

```
Frontend (MAUI)
-> GetThere API
-> TransitOrchestrator
-> ITransitRouter (country -> instance)
-> ITransitProvider
-> OtpTransitProvider
-> OtpClient (GraphQL HTTP)
-> OTP instance
```

This keeps OTP isolated and avoids coupling business logic to OTP schema.

---

## What Was Completed in Phase 1

- Replaced backend transit data flow with **OTP GraphQL** through a dedicated client.
- Added transit abstraction:
  - `ITransitProvider`
  - `OtpTransitProvider` (current implementation)
  - `ITransitRouter`
  - `TransitRouter` (country → OTP instance mapping)
  - `TransitOrchestrator` (global orchestration entry-point for transit provider calls)
- Rewrote `OperatorManager` to a thin orchestration layer for transit endpoints.
- Added OTP instance configuration and country mapping in `GetThereAPI/appsettings.json`.
- Removed GTFS parser stack and realtime vehicle tracking stack from API:
  - Removed static/realtime managers and parser files.
  - Removed vehicle/trip endpoints from transit API surface.
- Updated frontend map flow for OTP phase 1 behavior:
  - Stops, routes, and stop schedules remain active.
  - Vehicle polling and trip-detail fetch flow removed from MAUI map page.
- Delay handling: if realtime data is unavailable, delay fields remain `null`.

---

## What Remains to Be Done

- Validate production OTP GraphQL field compatibility against target OTP version/feed setup.
- Add robust pagination/limits for large stop and route datasets if needed per region.
- Add explicit endpoint/contract versioning strategy if public API clients will be external.
- Optional: add cache invalidation strategy for frontend local stop cache refresh intervals.

---

## Decisions Made

| Decision | Choice |
|---|---|
| Query protocol | GraphQL only |
| Instance mapping | Exists now |
| Route planning methods | Defined but not implemented |
| Transit orchestrator | Introduced now |
| Caching | Frontend only |
| Transitland | Removed |
| Delay when no realtime | Return `null` |
| Breaking changes | Allowed |
| Abstraction scope | Minimal |

---

## API Behavior Rules

- GraphQL only for OTP
- Return `delay = null` if realtime unavailable
- Breaking API changes allowed
- DTOs act as stable contracts

---

## How to Extend the Current Code Safely

### Add Another OTP Region (Multi-instance)
1. Add a new entry under `Otp:Instances` in `appsettings`.
2. Add country IDs under `Otp:CountryInstanceMap` pointing to that instance key.
3. No manager/controller refactor required.

### Add a New Transit Provider
1. Create a new provider implementing `ITransitProvider`.
2. Update routing/orchestration to pick provider by policy (region/capability).
3. Keep controller and frontend DTO contracts stable.

### Add Route Planning
- Route-planning placeholders are intentionally documented in `ITransitProvider`.
- Add dedicated route-planning DTOs first, then implement provider methods in orchestrator/provider.

### Add Flights
1. Create flight provider.
2. Implement API adapter.
3. Extend orchestrator.

### Add GBFS
1. Create GBFS provider.
2. Add provider per vendor.
3. Integrate into orchestrator.

### Add AI Routing
1. Implement route planning.
2. Add scoring logic.
3. Rank routes in orchestrator.

---

## Future Requirements (Do Not Implement Now)

### Architecture Target State
```
GetThere API
↓
Transit Orchestrator (future)
↓
OTP | Flights | GBFS
```

### Multi-region OTP
- OTP-EU, OTP-US, OTP-ASIA
- Instance mapping must exist now

### Global Routing
Trips will include:
- OTP (origin) → Flight API → OTP (destination)

### AI Routing Layer
- Fastest route
- Cheapest route
- Balanced route
- AI scoring

### GBFS System
- Bikes and scooters
- Separate from OTP
- Adapter per provider

---

## Next Phases (Planned)

### Phase 2: Multi-region OTP Routing
- Expand router to support EU/US/ASIA instance selection policies.
- Add health and fallback logic per instance.

### Phase 3: Global Multimodal Orchestration
- Add flights provider behind a separate abstraction.
- Compose multi-leg itineraries: ground → flight → ground.

### Phase 4: AI Optimization Layer
- Add ranking/scoring policies (fastest, cheapest, balanced, AI-ranked).
- Keep provider access behind orchestrator interfaces.

### Phase 5: GBFS Ecosystem
- Add GBFS provider abstraction separate from transit provider.
- Integrate bikes/scooters as a parallel provider family.

---

## Key Guardrails for Future Work

- Do not reintroduce GTFS parsing in GetThere backend.
- Do not couple controllers/business logic directly to OTP GraphQL schema.
- Keep provider abstractions minimal and explicit.
- Keep frontend contracts DTO-based (not raw provider payloads).
- Keep backend stateless.
- Avoid overengineering.
- Always use providers — do not bypass abstraction.

---

## Success Criteria (Phase 1)

- OTP running
- Stops load correctly
- Schedules returned
- Realtime delays shown
- No GTFS parsing remains
- Clean abstraction layer exists

---

## One-Line Summary

> Replace all GTFS and Transitland logic with OTP behind a clean provider abstraction, and structure the system for future multi-region, flights, GBFS, and AI routing — without refactoring.
