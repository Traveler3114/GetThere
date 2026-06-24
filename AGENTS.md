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

## Session — June 24, 2026

### Applied (TransitInfoAPI)
| Issue | File(s) | What |
|-------|---------|------|
| #3 | `FeedManager.cs`, `TransitDbContext.cs` | FeedId uniqueness (index + validation in CreateAsync) |
| #24 | `ScheduleManager.cs` | Bounding box lon buffer × cos(lat) |
| #27 | `ReconciliationManager.cs` (×2) | Geometry on CanonicalStation creation |
| #33 | `GtfsParser.cs` | Logging for invalid PickupType/DropOffType |
| #35 | `FeedVersionMapper.cs`, `FeedVersionContract.cs` | AgencyCount on response |
| #37 | `GtfsParser.cs` | Math.Round fractional seconds |
| #46/47 | `GtfsParser.cs`, `GtfsParserManager.cs` → `GtfsParser`, `ImportLogStore.cs` | Rename + namespace fix |
| #48 | `PlaceMatchingManager.cs` | Geohash boundary comment |
| #49 | `OnestopIdManager.cs` | Abbreviation expansion in NormalizeName |
| #50 | — | Removed CalculateDistanceMeters wrapper, use GeoUtils directly |
| #56 | `TransitDbContext.cs`, migration | Filtered unique index on FeedVersion (IsActive=1) |
| #59 | `feeds.html` | Health check batch 5→20 |
| #60 | `reconciliation.html` | Parallel batch approve (Promise.all) |
| #61 | `reconciliation-map.html` | Preload map icons |
| #62 | `reconciliation-map.html` | SVG vehicle bearing marker |
| #63 | `reconciliation.html`, `feeds.html` | Normalized CSV error handling |
| #66 | `OperatorManager.cs` | .Take(500) on GetRoutesAsync |
| #67 | `GtfsParser.cs` | Separate SqlConnection for bulk copy |
| #68 | `RealtimeManager.cs` | Stream protobuf (no MemoryStream) |
| #86 | — | Route type map consolidation verified |
| #94 | `ImportLogStore.cs` | In-memory limitation doc |
| #99 | `RouteContract.cs` | Bidirectional route limitation doc |
| #101 | `StationMergeLog.cs`, `TransitDbContext.cs`, `ReconciliationManager.cs`, migration | StationMergeMovedRawStop join table |
| #104 | `Alert.cs`, `AlertMapper.cs` | CreatedAt field |
| #106 | `FeedManager.cs` | Direct SHA1 hash stream |
| #108 | `GtfsParser.cs` | calendar/calendar_dates required in ValidateGtfs |
| #132 | Migration `AddStationMergeLogRelations` | Fixed Down() nullable column |
| #138 | Contracts | XML docs on public contracts |
| #139 | `launchSettings.json` | HTTPS profile added |

### Reverted
| Issue | What | Why |
|-------|------|-----|
| #51 | IDbContextFactory | DI lifetime conflict — reverted to IServiceScopeFactory |

### Blocked
- **#26** — Re-reconciliation endpoint (non-trivial, deferred)
- **#36** — CanonicalRoute GlobalId embeds FeedId (policy)
- **#87** — GBFS polling (skipped)
- **#89** — ApiKey plain text (skipped)

## Reference

`PROJECT.md` is the canonical conventions doc (architecture, code style, response formats, pagination, endpoint patterns). `GetThereAPI/Program.cs` shows the DI wiring and middleware order.
