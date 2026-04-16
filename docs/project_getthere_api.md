# GetThereAPI — Full Code Documentation

## 1) Project role in the solution
`GetThereAPI` is the core backend API and domain service for user/account, wallet/payment/tickets, operator metadata, mobility caching, and OTP-transit orchestration.

It is:
- a business API consumed by the MAUI app,
- and includes a temporary migration bridge endpoint consumed by `OpenTripPlannerAPI` only when `OtpConfigSource=Http`.

---

## 2) Runtime and platform details
- Project file: `GetThereAPI/GetThereAPI.csproj`
- SDK: `Microsoft.NET.Sdk.Web`
- Target framework: `net10.0`
- Primary packages:
  - ASP.NET Core JWT bearer auth
  - ASP.NET Identity + EF stores
  - EF Core SQL Server + tools
  - OpenAPI + Scalar UI

---

## 3) Startup pipeline (`Program.cs`)

### Service registration summary
- Controllers + OpenAPI.
- `AppDbContext` using SQL Server `DefaultConnection`.
- Identity with password policy and unique-email rule.
- Transit stack:
  - `OtpClient`
  - `ITransitProvider -> OtpTransitProvider`
  - `ITransitRouter -> TransitRouter`
  - `TransitOrchestrator`
- `MobilityManager` as singleton + hosted background service.
- Reflection-based auto-registration for all managers in namespace except `MobilityManager` (already singleton).
- JWT bearer authentication.
- CORS policy `MapAssets` allowing all origins/methods/headers (for map image fetch scenarios from MAUI WebView).

### App middleware order
1. Development-only OpenAPI + Scalar.
2. `UseCors("MapAssets")`
3. `UseStaticFiles()`
4. `UseHttpsRedirection()`
5. `UseAuthentication()`
6. `UseAuthorization()`
7. `MapControllers()`

### Background init detail
A fire-and-forget startup task calls `MobilityManager.InitialiseAsync()` so initial station cache is primed early.

---

## 4) Configuration (`appsettings.json`)

### Main sections
- `ConnectionStrings:DefaultConnection`
- `Jwt`:
  - key, issuer, audience, expiry
- `Otp`:
  - `DefaultInstance`
  - `Instances` dictionary with base URL + GraphQL path

---

## 5) Data model and persistence
Primary context: `Data/AppDbContext.cs`

### DbSets
- Wallets
- WalletTransactions
- Tickets
- Payments
- TransitOperators
- TransportTypes
- PaymentProviders
- Countries
- Cities
- MobilityProviders

### Important model behaviors
- All enum properties are automatically persisted as strings (converter applied by model iteration).
- Seed data is included for countries, cities, operators, transport types, payment providers, mobility provider.
- Mobility many-to-many join tables are configured:
  - `MobilityProviderCountry`
  - `MobilityProviderCity`

### Entity responsibilities (high-level)
- `AppUser`: identity user extension with `FullName`, timestamps.
- `Wallet`: per-user balance.
- `WalletTransaction`: history ledger.
- `Payment`: top-up/payment transaction log.
- `Ticket`: purchased ticket records.
- `TransitOperator`: operator identity + static/realtime GTFS URL references.
- `MobilityProvider`: provider feed metadata for bike/scooter-like sources.

---

## 6) API controller surface

### AuthController (`/auth`)
- `POST /auth/register`
- `POST /auth/login`

### CountryController (`/countries`)
- `GET /countries`

### OperatorController (`/operator`)
- `GET /operator`
- `GET /operator/ticketable`
- `GET /operator/stops`
- `GET /operator/routes`
- `GET /operator/stops/{stopId}/schedule`
- `GET /operator/health`
- `GET /operator/transport-types`
- `GET /operator/otp-feeds` (migration/rollback bridge for OTP config source)

### MapController (`/map`)
- `GET /map/features`
- `GET /map/bike-stations`

### WalletController (`/wallet`, authorized)
- `GET /wallet`
- `GET /wallet/transactions`

### PaymentController (`/payment`, authorized)
- `GET /payment/providers`
- `POST /payment/topup`

### TicketController (`/ticket`, authorized)
- `GET /ticket`

### MockTicketController (`/mock-tickets`)
- `GET /mock-tickets/{operatorId}/options`
- `POST /mock-tickets/{operatorId}/purchase` (authorized)

---

## 7) Manager layer responsibilities

### OperatorManager
- Lists operators.
- Produces ticketable operator list (including mobility-aware country filtering).
- Delegates stops/routes/schedules/health checks to transit orchestrator.
- Produces OTP feed metadata used by OpenTripPlannerAPI.

### MobilityManager
- Background service polling all configured mobility providers every 2 minutes.
- Parses provider feeds through parser factory.
- Caches station lists in memory per provider.
- Exposes read/filter helper methods for map/ticketable usage.

### PaymentManager
- Wallet top-up operation.
- Creates payment records.
- Adds wallet transaction history entry.

### WalletManager
- Creates wallet on registration.
- Reads wallet and wallet transaction history.

### TicketManager
- Returns purchased tickets for requesting user.

### TokenManager
- Issues JWT signed with HMAC SHA-256.

---

## 8) Transit abstraction stack
Location: `GetThereAPI/Transit`

### Why abstraction exists
To isolate OTP GraphQL schema from controllers and keep future provider swaps possible.

### Components
- `ITransitProvider`: stops/routes/schedule/health contract.
- `OtpTransitProvider`: OTP GraphQL implementation.
- `ITransitRouter` + `TransitRouter`: chooses instance key by country context.
- `TransitOrchestrator`: orchestration entry used by managers.
- `OtpClient`: raw GraphQL HTTP query client.

### Current routing behavior
`TransitRouter.ResolveInstanceKeyAsync` currently returns default instance (`Otp:DefaultInstance`) with country-aware guard checks in place for future expansion.

### OtpTransitProvider behavior details
- Stops query with route mode fallback logic.
- Routes query with mode-to-GTFS-route-type mapping.
- Stop schedule query using `stoptimesWithoutPatterns` grouped by route/headsign.
- Delay computation from `scheduledDeparture` vs `realtimeDeparture`.
- Health check uses minimal GraphQL typename query.

---

## 9) Security model

### Authentication
- JWT bearer for protected endpoints.
- User identity taken from `sub` claim.

### Authorization
- User-scoped controllers use `[Authorize]`.
- Public endpoints are limited to discovery/transit metadata as currently implemented.

### Data handling considerations
- Sensitive operator internal fields are not exposed by public DTOs.
- Mock purchase flow validates wallet balance before mutation and executes in DB transaction.

---

## 10) Background and cache behavior
- `MobilityManager` initial load is executed on startup.
- Poll loop continues with fixed interval.
- On fetch failure, stale data is preserved rather than dropped.

---

## 11) How future backend code should be implemented

### Rule 1 — controller thinness
Controllers should only:
- parse input,
- call manager/service,
- map HTTP status/result wrappers.

Business logic belongs in manager/services.

### Rule 2 — contract stability
- Add/modify DTOs in `GetThereShared` first.
- Prefer additive changes.
- Avoid breaking renames/removals without migration strategy.

### Rule 3 — transactional money/ticket operations
Any workflow changing wallet/ticket/payment state must use atomic transaction semantics.

### Rule 4 — provider decoupling
Do not call OTP directly from controllers/managers outside transit provider stack.

### Rule 5 — extension through abstraction
- New transit provider -> implement `ITransitProvider`.
- New routing strategy -> expand `ITransitRouter` implementation.
- Keep API contracts stable while internals evolve.

---

## 12) Recommended engineering backlog
- Add explicit validation attributes/filters for request DTOs.
- Add API-level integration tests for auth/topup/purchase/transit schedule.
- Add structured logs with correlation IDs.
- Add concurrency safeguards for wallet row updates at scale.
- Expand transit instance routing policy for true multi-region behavior.
