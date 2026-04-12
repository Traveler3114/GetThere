# OTP Phase 1 Status, Extension Guide, and Next Steps

## 1) What was completed in this phase

- Replaced backend transit data flow with **OTP GraphQL** through a dedicated client.
- Added transit abstraction:
  - `ITransitProvider`
  - `OtpTransitProvider` (current implementation)
  - `ITransitRouter`
  - `TransitRouter` (country -> OTP instance mapping)
  - `TransitOrchestrator` (global orchestration entry-point for transit provider calls)
- Rewrote `OperatorManager` to a thin orchestration layer for transit endpoints.
- Added OTP instance configuration and country mapping in:
  - `/home/runner/work/GetThere/GetThere/GetThereAPI/appsettings.json`
- Removed GTFS parser stack and realtime vehicle tracking stack from API:
  - Removed static/realtime managers and parser files.
  - Removed vehicle/trip endpoints from transit API surface.
- Updated frontend map flow for OTP phase-1 behavior:
  - Stops + routes + stop schedules remain active.
  - Vehicle polling and trip-detail fetch flow removed from MAUI map page.
- Delay handling behavior:
  - If realtime data is unavailable, delay fields remain `null`.

---

## 2) Current architecture (phase 1 target)

Frontend (MAUI)  
-> GetThere API  
-> TransitOrchestrator  
-> ITransitRouter (country -> instance)  
-> ITransitProvider  
-> OtpTransitProvider  
-> OtpClient (GraphQL HTTP)  
-> OTP instance

This keeps OTP isolated and avoids coupling business logic to OTP schema.

---

## 3) What remains to be done

- Validate production OTP GraphQL field compatibility against target OTP version/feed setup.
- Add robust pagination/limits for large stop and route datasets if needed per region.
- Add explicit endpoint/contract versioning strategy if public API clients will be external.
- Optional: add cache invalidation strategy for frontend local stop cache refresh intervals.

---

## 4) How to extend the current code safely

### Add another OTP region (multi-instance)
1. Add a new entry under `Otp:Instances` in appsettings.
2. Add country IDs under `Otp:CountryInstanceMap` pointing to that instance key.
3. No manager/controller refactor should be required.

### Add a new transit provider later
1. Create a new provider implementing `ITransitProvider`.
2. Update routing/orchestration to pick provider by policy (region/capability).
3. Keep controller and frontend DTO contracts stable.

### Add route planning later
- Route-planning placeholders are intentionally documented in `ITransitProvider`.
- Add dedicated route-planning DTOs first, then implement provider methods in orchestrator/provider.

---

## 5) Next phases (planned)

### Phase 2: Multi-region OTP routing
- Expand router to support EU/US/ASIA instance selection policies.
- Add health and fallback logic per instance.

### Phase 3: Global multimodal orchestration
- Add flights provider behind a separate abstraction.
- Compose multi-leg itineraries: ground -> flight -> ground.

### Phase 4: AI optimization layer
- Add ranking/scoring policies (fastest, cheapest, balanced, AI-ranked).
- Keep provider access behind orchestrator interfaces.

### Phase 5: GBFS ecosystem
- Add GBFS provider abstraction separate from transit provider.
- Integrate bikes/scooters as a parallel provider family.

---

## 6) Key guardrails for future work

- Do not reintroduce GTFS parsing in GetThere backend.
- Do not couple controllers/business logic directly to OTP GraphQL schema.
- Keep provider abstractions minimal and explicit.
- Keep frontend contracts DTO-based (not raw provider payloads).
