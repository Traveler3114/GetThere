# GetThere Solution Integration — End-to-End Architecture and Future Development Guide

## 1) Solution composition
Solution file: `GetThere.slnx`

Projects:
1. `GetThere` (MAUI client)
2. `GetThereAPI` (core backend API)
3. `GetThereShared` (shared contracts)
4. `OpenTripPlannerAPI` (scraper + GTFS-RT host + OTP config generator)

---

## 2) How projects work together (big-picture)

### A) User-facing app features
`GetThere` -> `GetThereAPI`
- Authentication, wallet, payments, tickets, country selection, shop catalog.

### B) Transit map and schedule features
`GetThere` -> `GetThereAPI` -> OTP GraphQL via transit abstraction.

### C) Realtime GTFS feed pipeline
`OpenTripPlannerAPI` scrapes realtime -> serves GTFS-RT feed endpoint(s).

### D) OTP feed config source
`OpenTripPlannerAPI` -> calls `GetThereAPI /operator/otp-feeds` -> generates OTP config files.

### E) Shared contract alignment
`GetThere` and `GetThereAPI` both reference `GetThereShared` so payload models remain synchronized.

---

## 3) End-to-end runtime flows

## 3.1 Authentication + secured API flow
1. User logs in from `GetThere`.
2. `GetThereAPI` validates credentials and returns JWT.
3. App stores JWT in secure storage.
4. `AuthenticatedHttpHandler` injects token for secured calls.
5. Protected endpoints authorize by JWT claims.

## 3.2 Wallet top-up flow
1. App calls `POST /payment/topup`.
2. API validates request and user identity.
3. Payment record created.
4. Wallet balance updated.
5. Wallet transaction history row inserted.
6. Updated wallet DTO returned.

## 3.3 Mock ticket purchase flow
1. App fetches ticketable operators/options.
2. User submits purchase request.
3. API performs atomic transaction:
   - verifies balance,
   - deducts balance,
   - persists ticket (for mapped transit operators),
   - writes wallet transaction.
4. App receives mock ticket result + can display in ticket confirmation flows.

## 3.4 Transit map static bootstrap
1. Map page loads HTML/CSS/JS bundle.
2. Calls backend for transport types, stops, routes, bike stations.
3. Injects data into map JS render functions.
4. User interactions request schedule-on-demand per stop.

## 3.5 Realtime scraping/feeding flow
1. OpenTripPlannerAPI starts and loads DB-backed OTP feed config.
2. Scraper worker runs first cycle, updates in-memory feed bytes.
3. Feed endpoints return current protobuf datasets.
4. Status endpoint reports scrape progress/freshness.
5. OTP can be auto-started after first scrape cycle completes.

---

## 4) Data and contract boundaries (small-detail view)

### Boundary 1: MAUI <-> API
- Protocol: JSON over HTTP(S)
- Contract source: `GetThereShared` DTOs.
- Response wrapper convention: `OperationResult` / `OperationResult<T>`.

### Boundary 2: API <-> OTP
- Protocol: GraphQL over HTTP
- Encapsulation point: `OtpClient` + `ITransitProvider`.

### Boundary 3: OpenTripPlannerAPI <-> API
- Protocol: JSON HTTP call to `/operator/otp-feeds`
- Purpose: obtain operator static/realtime feed URLs and feed IDs.

### Boundary 4: External source <-> OpenTripPlannerAPI
- Protocol: HTTP scraping + GTFS ZIP download.
- Output conversion: GTFS-RT protobuf feed bytes.

---

## 5) Future code strategy — from smallest change to largest change

## 5.1 Small changes (safe incremental)
Examples:
- add a new field to existing DTO,
- add one non-breaking endpoint,
- add one extra map rendering property.

Recommended process:
1. Update shared DTO (if contract changes).
2. Update backend output.
3. Update app parsing/usage.
4. Keep old behavior backward compatible.

## 5.2 Medium changes (feature additions)
Examples:
- new app page + backend domain endpoint,
- new map feature type,
- extended wallet/ticket views.

Recommended process:
1. Define shared DTOs and response contracts.
2. Implement backend manager logic.
3. Add thin controller endpoints.
4. Add MAUI service methods.
5. Add UI and loading/error states.
6. Add tests for feature path.

## 5.3 Large changes (architecture evolution)
Examples:
- multi-region OTP routing policies,
- additional transit providers,
- non-mock ticket provider integrations,
- mobility ecosystem expansion (scooters/mopeds/GBFS variants).

Recommended process:
1. Keep provider abstractions as boundaries (`ITransitProvider`, parser interfaces).
2. Add new implementation modules without breaking existing interfaces.
3. Add routing/policy selection in orchestrators.
4. Maintain stable API contracts or introduce explicit versioning.
5. Add migration/rollback strategy for production data changes.

---

## 6) Future architecture extension map

### A) Transit
- Add more OTP instances and country-to-instance routing logic.
- Add route planning APIs and DTOs behind existing transit abstraction.

### B) Mobility
- Add new mobility feed parsers (GBFS, proprietary APIs).
- Keep caching behavior and provider-based filtering model.

### C) Ticketing
- Replace mock purchase with real provider adapters while preserving app-facing contract shape where possible.

### D) Observability
- Add unified telemetry/correlation IDs across app requests, API calls, and scraper logs.

### E) Security hardening
- Remove dev-only TLS bypass from production app client path.
- Add stricter input validation and optional rate limiting on sensitive endpoints.

---

## 7) Practical coding standards for future contributors

1. **Contract-first**: if payload changes, update `GetThereShared` intentionally.
2. **Thin controllers**: backend business logic belongs in managers/services.
3. **Provider abstraction**: external systems should be wrapped behind interfaces.
4. **Transactional integrity**: wallet/ticket/payment mutations must be atomic.
5. **Backward compatibility**: prefer additive API changes.
6. **Defensive UI**: app should treat network and partial data failures as expected states.
7. **No duplicate model definitions** across projects.
8. **Explicit extension points**: add features by module (service, parser, provider), not by patching unrelated layers.

---

## 8) Suggested phased roadmap for “future code done right”

### Phase 1
- Expand tests for existing auth/wallet/shop/map flows.
- Add validation/guardrails on API input.

### Phase 2
- Implement richer transit routing policy (multi-instance).
- Introduce cache and timeout policy tuning for OTP calls.

### Phase 3
- Integrate real ticket providers via adapter interfaces.
- Keep current mock flow as fallback/test mode.

### Phase 4
- Expand mobility providers and unify multimodal planning orchestration.

### Phase 5
- Introduce production-grade observability and SLO monitoring for all services.

---

## 9) Final summary
This solution already has a strong modular direction:
- shared contracts,
- backend orchestration layers,
- dedicated realtime scraper host,
- client service abstraction.

The key to scaling safely is to preserve boundaries and evolve through explicit extension points, not cross-layer shortcuts.
