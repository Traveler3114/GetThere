# GetThere — Agent Guide

## Architecture

**Two platforms, one-way dependency:**
- `TransitInfoAPI` (map: GTFS feeds, stations, reconciliation, mobility) → port 5000 (`http`) / 5001 (`https`), DB: `TransitInfoDB`
- `GetThereAPI` (business: users, wallets, ticketing) → port 7230, DB: `GetThereDB`
- `GetThere` (MAUI client) → calls GetThereAPI for all business data, and reads TransitInfoAPI
  directly **for the map only**
- `GetThereShared` → shared DTOs/contracts, no runtime. Referenced by the MAUI app, so nothing
  server-side belongs in it
- `SharedAuth` → server-side invariants shared by both APIs: token signing/hashing, refresh-token
  reuse rules, the account-enumeration guard, telemetry registration. No DbContext, no entities.
  Keep it invariant — anything either service might want to change on its own schedule does not
  belong here

One-way rule: TransitInfoAPI knows nothing about GetThereAPI. GetThereAPI references operators by TransitInfoAPI GlobalId.

**The map and journey planning are the client-side exception.** `MapPage` is a WebView pointed at
`map/public.html` on TransitInfoAPI, so the page is same-origin with the data it reads and needs no
proxy, no bearer token and no CORS. The endpoints it uses (`stations`, `routes`,
`mobility/stations`, `stations/{id}/departures`, `realtime/vehicles`, `stations/search`, `plan`) are
`[AllowAnonymous]` for that reason — treat them as a public surface when changing them. `plan`
(door-to-door routing via OTP) joined this set with the routing engine: the client reads it directly
from TransitInfoAPI, same-origin, exactly like the map endpoints, and reads each transit leg's
operator GlobalId to join GetThereAPI ticketing itself — neither server calls the other, so the
one-way rule holds. Everything else the client does still goes exclusively through GetThereAPI. The
map arrangement replaced a proxy in GetThereAPI; `docs/map-proxy-migration.md` records why it was
undone.

The OTP graph is built only from TransitInfoAPI's own export (`Routing/Export/` → the merged GTFS
bundle + GBFS), never from raw operator feeds, so reconciliation reaches routing. The export-serving
and GBFS endpoints under `routing/` are consumed by OTP (server-side, `[Authorize]`), not the
client, and so are not part of the exception above.

The client's map address is `Map:BaseUrl` (see `ApiEndpoints`), and it must be **https** — the
Android manifest sets `usesCleartextTraffic="false"`, so TransitInfoAPI's `http` profile fails
silently on device.

**One-way JavaScript→native handoff — the only bridge left.** The map WebView emits the selected
routed itinerary to the app: `planner.js`'s "Get tickets" button navigates to the
`gtapp://journey?legs=…` custom scheme, `MapPage`'s `Navigating` handler cancels that navigation,
deserializes the compact legs, stores them on the `JourneyHandoff` singleton, and the native
`BuyJourneyPage` prices and buys them through GetThereAPI (quote → breakdown + total, book →
purchases + wallet holds). This is distinct from — and deliberately does **not** revive — the
removed native→JavaScript chrome/token bridge (the `EvaluateJavaScriptAsync` calls and bearer-token
injection the same-origin page made unnecessary): nothing is ever injected into the page, and the
WebView itself still never calls GetThereAPI.

**Separate auth domains, same key type.** Both APIs use `IdentityUser` with a string GUID key —
TransitInfoAPI was moved off `IdentityUser<int>` in Phase 0. They remain separate user stores in
separate databases with no cross-system user references.

**GetThereAPI makes no call to TransitInfoAPI.** The service-account bridge
(`getthere-api@transit.local`), `TransitInfoApiClient`, `MapProxyController`, `MapManager` and the
`map.view` permission were removed on 2026-08-02, once the map migration left them with no caller but
an admin status dot. Treat any reappearance as a regression — and note that GetThereAPI referencing
an operator by `TransitInfoGlobalId` is **not** that: it is a string soft reference, not a call.

The *seeding* of that account outlived the removal by eight days and was deleted on 2026-08-10:
TransitInfoAPI was still creating `getthere-api` on every boot, in the `Client` role, with no caller.
`Seed:ServiceAccountPassword` is no longer read by anything. **Existing databases still have the
row** — delete it, and check `AspNetUserRoles` for the stale grant.

## Running

**Order matters — API must be running before MAUI starts.**

```powershell
# Business API (must start first)
dotnet run --project GetThereAPI/GetThereAPI.csproj --launch-profile https

# Map platform — https, not the default http profile: the MAUI client loads the map page from
# here, and the Android manifest disallows cleartext.
dotnet run --project TransitInfoAPI/TransitInfoAPI.csproj --launch-profile https

# MAUI — Android
dotnet build GetThere/GetThere.csproj -t:Run -f net10.0-android

# MAUI — Windows
dotnet build -t:Run -f net10.0-windows10.0.19041.0
```

Android emulator reaches host via `https://10.0.2.2:7230/` (not `localhost`).

## EF Core Migrations

Stop the API first, then:

```powershell
cd GetThereAPI
dotnet ef migrations add <Name>
dotnet ef database update

# For TransitInfoAPI:
cd TransitInfoAPI
dotnet ef migrations add <Name>
dotnet ef database update
```

TransitInfoAPI runs `MigrateAsync()` on startup **in Development only**. Outside Development both
APIs expect migrations to be applied as a deploy step; set `Database:MigrateOnStartup=true` to opt
back in. Never manually edit `*ModelSnapshot.cs`.

## Code Conventions

| Rule | Standard |
|------|----------|
| Namespaces | File-scoped (`namespace X.Y;`) |
| Null checks | `is null` / `is not null` (not `==`/`!=`) |
| Collections | `[]` expressions (not `new List<T>()`) |
| Parsing | `TryParse` over `Parse` |
| Mappers | Static manual classes in `GetThereAPI/Mapping/` (no AutoMapper) |
| Cancellation | `CancellationToken ct = default` as **last** param on all async API methods; MAUI services don't use it |
| Enums | Stored as strings via `HasConversion<string>()` |
| Hard deletes | Never on operational records (tickets, wallets, payments) — use status flags |
| Validation | In the manager, never rely on SQL constraints as user-facing error |

### Manager pattern
Business logic in `GetThereAPI/Managers/` and `TransitInfoAPI/Managers/`. Controllers are thin — receive input, call manager, return result. **Controllers never catch exceptions** — let them bubble to the global exception handler.

### Auto-registration
- `GetThereAPI.Managers.*` — auto-registered as scoped
- MAUI `Pages` and `ViewModels` — auto-registered by reflection in `MauiProgram.cs`.
  `GetThere.Services.*` are **not**: each is registered by hand so it can be handed the named
  `HttpClient`. Add a registration when you add a service.
- TransitInfoAPI registers its managers explicitly in `Program.cs`; `MobilityManager` is **scoped**
  (it depends on `TransitDbContext`). The singletons there are `OnestopIdManager`, `RealtimeManager`,
  `ImportLogStore` and `ExternalFeedSource` — `RealtimeManager` resolves its DbContext per poll
  through `IServiceScopeFactory`
- `AdapterRegistry` (singleton) and `AuthService` in MAUI (singleton — it owns the token cache and
  the refresh lock)

## Off-limits (need human instruction)

- JWT auth pipeline (token creation/validation). Two deliberate changes have been authorised:
  - **2026-07-31** — refresh tokens are no longer rejected when presented from a different IP
    address. Rotation and reuse detection remain the theft response; the address is audited as
    `RefreshAddressChanged`. See
    [`docs/reference/getthere-api/architecture.md`](docs/reference/getthere-api/architecture.md#the-address-is-recorded-not-enforced)
  - **2026-08-10** — rotation is atomic. `RefreshAsync` claims the token with a single conditional
    `ExecuteUpdate` (`WHERE Id = … AND RevokedAt IS NULL AND ReplacedByToken IS NULL`) and treats a
    zero row count as reuse. It was a read-modify-write, so two concurrent presentations of one token
    both passed `Evaluate` as `Rotate` and both minted a valid successor — rotation only detects
    reuse if rotating is atomic. Applied identically in both APIs.

  The rest of the pipeline is still off-limits.
- Wallet balance deduction logic
- Ticket status transitions
- ImportedTicket status transitions
- EF Core migration auto-generated files
- Seed data removal

## Change log

Per-session implementation detail — what changed, in which files, and why — lives in
[`docs/changelog.md`](docs/changelog.md). It used to sit here, but this file is read as context at
the start of every session and two thirds of it had become history rather than working knowledge.

`ROADMAP.md` is not a changelog either: it tracks phase deliverables, and its status marks mean
"exercised end-to-end" — see its Notes section.

## Reference

`PROJECT.md` is the canonical conventions doc (architecture, code style, response formats, pagination, endpoint patterns). `GetThereAPI/Program.cs` shows the DI wiring and middleware order.
