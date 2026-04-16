# GetThereAPI — Detailed Code Documentation

## 1. Purpose of this project
`GetThereAPI` is the main backend API for account, wallet, ticketing, map-support endpoints, operator metadata, and transit orchestration.

It is the primary system of record for:
- Users/Identity,
- Wallet balances and transactions,
- Payment records,
- Tickets,
- Operators, countries, cities, transport types, and mobility providers.

---

## 2. Technical architecture

### Runtime and hosting
- File: `GetThereAPI/Program.cs`
- ASP.NET Core Web API (`net10.0`).
- Uses:
  - EF Core + SQL Server,
  - ASP.NET Identity,
  - JWT bearer auth,
  - CORS policy for map image assets.

### Core modules
- `Controllers/` → HTTP API surface.
- `Managers/` → business logic / orchestration for each domain.
- `Data/AppDbContext.cs` → EF model, relationships, seed data.
- `Transit/` → OTP abstraction layer:
  - `ITransitProvider`, `OtpTransitProvider`,
  - `ITransitRouter`, `TransitRouter`,
  - `TransitOrchestrator`.

### Startup behavior
- Registers `MobilityManager` as singleton + hosted background service.
- Performs non-blocking background initialization for mobility station cache.

---

## 3. Important API areas

### Auth and identity
- `AuthController`:
  - `POST /auth/register`
  - `POST /auth/login`
- `TokenManager` creates JWT tokens with standard claims (`sub`, `email`, `given_name`, `jti`).

### Wallet, payments, tickets
- `WalletController`:
  - `GET /wallet`
  - `GET /wallet/transactions`
- `PaymentController`:
  - `GET /payment/providers`
  - `POST /payment/topup`
- `TicketController`:
  - `GET /ticket`

### Operator/transit/map
- `OperatorController`:
  - operators, ticketable operators, stops, routes, stop schedule, health, transport types, OTP feed config.
- `MapController`:
  - unified feature envelope endpoint + bike stations endpoint.
- `CountryController`:
  - country lookup data.

### Mock ticket purchase
- `MockTicketController`:
  - `GET /mock-tickets/{operatorId}/options`
  - `POST /mock-tickets/{operatorId}/purchase` (authorized)
- Uses DB transaction for atomic balance check + deduction + ticket/transaction persistence.

---

## 4. Transit integration design

The API intentionally isolates OTP specifics:
- Controller → `OperatorManager` → `TransitOrchestrator` → `ITransitProvider` → `OtpClient`.
- This keeps backend contracts stable when provider details evolve.

Current provider behavior:
- Stops and routes from OTP GraphQL.
- Stop schedule grouped by route/headsign with realtime-aware delay fields.
- Health check uses lightweight GraphQL query.

---

## 5. Data model and persistence behavior

### Database context
- `AppDbContext` includes identity and domain entities.
- Enum values are stored as strings via automatic converter setup.
- Seed data includes initial countries/cities/operators/payment providers/transport types.

### Operator feed model
- `TransitOperator` stores feed URLs for OTP integration:
  - `GtfsFeedUrl`
  - `GtfsRealtimeFeedUrl`

---

## 6. How future backend code should be done

### Keep contract-first discipline
For any new API capability:
1. Define/extend DTO in `GetThereShared`.
2. Implement manager logic.
3. Keep controller thin (mapping/request validation only).
4. Return `OperationResult` wrappers consistently.

### Keep provider abstraction clean
- Do not couple controllers/managers to raw OTP GraphQL schema.
- Add new transit/mobility providers behind interfaces.
- Keep `TransitOrchestrator` as the only routing/orchestration entry point.

### Concurrency and atomicity
- Any wallet/balance mutation must be transactional.
- Avoid split writes for ticket purchase/payment flows.

### Security standards
- Keep `[Authorize]` on user-specific endpoints.
- Use user id from JWT claims only.
- Never expose provider secrets through DTOs.

### Performance and caching
- Continue using background polling caches for slowly changing external data (mobility feeds).
- Add scoped caching around expensive OTP queries only through provider layer, not controllers.

---

## 7. Recommended next backend improvements
- Add input validation layer for request DTOs.
- Add optimistic concurrency/version fields for wallet and ticket entities.
- Add endpoint-level telemetry and structured operation IDs for debugging.
- Add automated tests for auth, wallet top-up, mock purchase, and transit schedule aggregation.
