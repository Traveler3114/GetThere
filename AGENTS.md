# GetThere — Agent Guide

## Architecture

**Two platforms, one-way dependency:**
- `TransitInfoAPI` (map: GTFS feeds, stations, reconciliation, mobility) → port 5000, DB: `TransitInfoDB`
- `GetThereAPI` (business: users, wallets, ticketing) → port 7230, DB: `GetThereDB`
- `GetThere` (MAUI client) → calls only GetThereAPI
- `GetThereShared` → shared DTOs/contracts, no runtime

One-way rule: TransitInfoAPI knows nothing about GetThereAPI. GetThereAPI references operators by TransitInfoAPI GlobalId.

## Running

**Order matters — API must be running before MAUI starts.**

```powershell
# Business API (must start first)
dotnet run --project GetThereAPI/GetThereAPI.csproj --launch-profile https

# Map platform
dotnet run --project TransitInfoAPI/TransitInfoAPI.csproj

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

TransitInfoAPI auto-runs `MigrateAsync()` on startup. Never manually edit `*ModelSnapshot.cs`.

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
- `GetThere.Services.*` — auto-registered by reflection in `MauiProgram.cs`
- Exceptions (explicitly registered): `MobilityManager` (singleton), `AdapterRegistry` (singleton)

## Off-limits (need human instruction)

- JWT auth pipeline (token creation/validation)
- Wallet balance deduction logic
- Ticket status transitions
- EF Core migration auto-generated files
- Seed data removal

## Session — June 24-25, 2026

### Applied (TransitInfoAPI) — Phase 1-6 sweep
| Phase | Issue | File(s) | What |
|-------|-------|---------|------|
| 1 | #7 | `OperatorManager.cs`, `OperatorsController.cs` | GetTotalCountAsync added |
| 1 | #10 | `RealtimeManager.cs` | Alert dedup key widened (incl. trip/agency IDs) |
| 1 | #12 | `FeedVersionsController.cs` | GetStops paginated |
| 1 | #128 | `wwwroot/map/index.html` | Vehicle fetch error shown |
| 2 | #111 | `wwwroot/admin/feeds.html` | AbortController 120s timeout on import |
| 3 | #112 | `RealtimeManager.cs` | Failure counter atomic (lock-based) |
| 3 | #58/92 | `wwwroot/map/index.html` | vehiclesInterval scoping fixed, pagehide cleanup |
| 4 | #114/115 | `wwwroot/admin/mobility.html` | const pageSize → let |
| 6 | #42 | `RealtimeManager.cs` | volatile on _tripUpdateCache |
| 6 | #55 | All Controllers/*.cs | [Range(1,500)] on perPage params |
| 6 | #105 | `FeedManager.cs` | Directory.CreateDirectory try/catch |
| 6 | #133 | `Program.cs` | Exception handler hides SQL details |
| 6 | #20 | `FeedManager.cs` | BeginImportTransactionAsync skips UseTransaction when tx exists |
| 6 | #34 | `PlaceMatchingManager.cs` | MatchStationsToPlacesAsync cooldown via PlaceMatchingOptions.CooldownHours |
| 6 | #40 | `FeedManager.cs` | BackfillRouteGeometriesAsync two-step LINQ avoids client eval |
| 6 | #52 | `OperatorContract.cs` | [MinLength(1)] on UpdateOperatorRequest.Name |
| 6 | #69 | `FeedManager.cs` | Log warning for non-.zip static feed URLs |
| 6 | #140 | `GeoJsonContract.cs`, `GeoJsonGeometry.cs` | Typed GeoJson geometry classes replace anonymous types |
| — | — | `ReconciliationManager.cs` | Spatial grid index (~0.2°), pre-bucket station lookup |
| — | — | `PlaceMatchingManager.cs` | 0.5° grid-cell spatial index for FindNearestPlace |
| — | — | `FeedManager.cs` | ReconcileAndBackfillAsync moved outside SQL transaction |
| — | — | `FeedManager.cs` | UseTransaction(null) after commit to clear EF Core tx ref |
| — | — | `FeedManager.cs` | Re-fetch FeedVersion after semaphore lock + skip if already Success |
| — | — | `FeedManager.cs` | feedLock.WaitAsync(CancellationToken.None) so manual trigger waits |
| — | — | `FeedManager.cs` | Command timeout 600s for StopTimes backfill UPDATE |
| — | — | `FeedManager.cs` | Early return in TriggerImportAsync when already Success |
| — | — | `PlaceMatchingManager.cs` | Fixed DeriveCountryIdAsync to use scoped DbContext |
| — | — | `FeedPollingWorker.cs` | Parallel.ForEachAsync(maxDegreeOfParallelism: 3) |
| — | — | `ScheduleManager.cs` | Fixed GetRouteStopsAsync LINQ GroupBy translation |
| — | — | `shape-editor.html` | Removed map.once('idle') wrapper, direct_select default mode fix |
| — | — | `FeedManager.cs` | Reactivation query (line 1194) broadened to cover all operators |

## Reference

`PROJECT.md` is the canonical conventions doc (architecture, code style, response formats, pagination, endpoint patterns). `GetThereAPI/Program.cs` shows the DI wiring and middleware order.
