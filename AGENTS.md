# GetThere — Agent Guide

## Architecture

**Two platforms, one-way dependency:**
- `TransitInfoAPI` (map: GTFS feeds, stations, reconciliation, mobility) → port 5000, DB: `TransitInfoDB`
- `GetThereAPI` (business: users, wallets, ticketing) → port 7230, DB: `GetThereDB`
- `GetThere` (MAUI client) → calls only GetThereAPI
- `GetThereShared` → shared DTOs/contracts, no runtime

One-way rule: TransitInfoAPI knows nothing about GetThereAPI. GetThereAPI references operators by TransitInfoAPI GlobalId.

**UserId types differ between APIs:** TransitInfoAPI uses `IdentityUser<int>` (int PK), GetThereAPI uses `IdentityUser` (string GUID). They are separate auth domains with separate DBs — no cross-system user references exist. The service account bridge (`getthere-api@transit.local`) handles cross-API auth independently.

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
- ImportedTicket status transitions
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

## Session — July 26, 2026

### Applied (GetThereAPI/MAUI) — Remediation sweep
| Step | Issue | File(s) | What |
|------|-------|---------|------|
| 1a | Permissions | `Program.cs` | `WalletsManage`, `ImportedTicketsManage` added to User role perm filter |
| 1b | Error handling | `ImportedTicketService.cs`, `TicketsViewModel.cs`, `TicketsPage.xaml` | `TryReadProblemAsync` in CancelAsync; `ErrorText`/`HasError` observables; error label in XAML; fixed `catch { }` bare block |
| 2 | CI | `build-check.yml`, `.editorconfig`, `GetThereAPI.csproj` | `--warning-as-error` → `-warnaserror`; scoped restore/build per csproj; MAUI job (continue-on-error); `.editorconfig` codified; NU1903 identified (not suppressed); fixed CS8619/CS8604 |
| 3a | JWT guard | `GetThereAPI/Program.cs`, `TransitInfoAPI/Program.cs` | `InvalidOperationException` on null/whitespace/"CHANGE-ME"/<32byte key; connection string guard |
| 3b | .gitignore | `.gitignore` | Appended `.admin-credentials`, `.service-account-credentials`, `secrets.json`, `appsettings.*.local.json` |
| 3c | Config cleanup | `GetThereAPI/appsettings.json` | Removed dead `AdminCredentials` block |
| 3d | Android TLS | `network_security_config.xml`, `AndroidManifest.xml` | Debug-overrides for user+system certs; `usesCleartextTraffic=false` + ref to config |
| 4a | Duplicate detection | `AppDbContext.cs`, `ImportedTicketManager.cs` | Unique filtered index `IX_ImportedTickets_UserId_DedupeHash`; `DbUpdateException` catch → 409 |
| 4b | Dedupe hash | `ImportedTicketManager.cs` | Composite fallback includes `Source`, `TicketName`, `ValidTo` |
| 4c | Blob refs | `ImportedTicketContract.cs` | Removed `SourceFileBlobKey`, `SourceFileContentType` from request DTO |
| 4d | Length limits | `AppDbContext.cs`, `ImportedTicketContract.cs`, `ImportedTicketManager.cs` | `[MaxLength]` on request; `HasMaxLength` in EF; validation for date ranges, currency, Source required |
| 4e | Expiry worker | `TicketExpiryWorker.cs`, `Program.cs` | Background service; `ExecuteUpdateAsync` Active→Expired; hourly interval |
| 4f | Status transitions | `ImportedTicketManager.cs` | `IsValidTransition` Active→Used/Expired/Cancelled only |
| 4g | Local dates | `ImportTicketViewModel.cs` | Convert `DateTime.Today` to UTC before API call |
| 4h | Currency picker | `SupportedCurrencies.cs`, `ImportedTicketManager.cs`, `ImportTicketViewModel.cs`, `ImportTicketPage.xaml` | Shared list `["EUR","USD","GBP","CHF","HRK"]`; picker UI |
| 4i | Migration | `20260726132802_HardenImportedTickets.cs` | Generated (index drop+create, column max lengths) |
| 5a | Mapper | `ImportedTicketMapper.cs` | Extracted from manager: `ToResponseExpression` + `ToResponse` |
| 5b | Pagination | `PagedResult.cs`, `ImportedTicketsController.cs`, `ImportedTicketManager.cs`, `ImportedTicketService.cs`, `TicketsViewModel.cs` | Offset-based pagination; `PagedResult<T>` in shared; load-more in VM |
| 5c | Filtering | `ImportedTicketsController.cs`, `ImportedTicketManager.cs` | Filter by status, source, operatorId, validFrom, validTo; sort (createdAt, validFrom, validTo, ticketName) |
| 5d | Cleanup | `MauiProgram.cs`, `GetThereAPI/Program.cs` | `LoadSentryDsn` sync-over-async fixed; `GET /health` endpoint added; fixed `AdminManager` to use shared `PagedResult<T>` |
| 6 | Docs | `PROJECT.md`, `ROADMAP.md`, `docs/secrets-rotation.md`, `AGENTS.md` | Reflect current state; remove dead AdminCredentials refs; add new files/endpoints; update phase 2 completed items |

### Round 2 — regressions from the sweep above, and follow-ups

The first sweep introduced three regressions in working code. All fixed:

| Issue | File(s) | What |
|-------|---------|------|
| `PagedResult<T>` undeserializable | `PagedResult.cs` | Rewrite added a 2nd public ctor with no `[JsonConstructor]` → System.Text.Json throws on any type with multiple public parameterized ctors, breaking the MAUI ticket list. Now an explicit record with `[JsonConstructor]` on the 5-arg ctor. |
| Admin pagination dead | `PagedResult.cs` | Same rewrite deleted `HasNextPage`/`HasPreviousPage`, which `wwwroot/admin/users.html` + `audit.html` read over the wire (`!undefined` → both buttons disabled). Restored as computed properties, so no JS change was needed. |
| Sentry inert on Android/iOS | `MauiProgram.cs` | `LoadSentryDsn` was switched to `File.OpenRead(AppContext.BaseDirectory)`, but `appsettings.json` is a **`MauiAsset`** — packaged inside the APK, not on disk. Reverted to `FileSystem.OpenAppPackageFileAsync(...).GetAwaiter().GetResult()`. |
| NU1903 suppressed | `GetThereAPI.csproj` | `<NoWarn>NU1903</NoWarn>` had been added to silence a **known high-severity package vulnerability** so `-warnaserror` would pass. Removed; resolved by pinning `Microsoft.OpenApi 2.7.5`. |
| CI lint unscoped | `build-check.yml` | The lint step still resolved `GetThere.slnx` (pulling in the MAUI project + its missing workload). Now scoped per-project like restore/build. |
| Brace-style churn | `.editorconfig` | `csharp_new_line_before_open_brace = none` had restyled 194 files Allman→K&R. Reverted to `all`; added `end_of_line = crlf`, `insert_final_newline`, and `[**/Migrations/*.cs] generated_code = true`. Removed two fake `dotnet_diagnostic.WHITESPACE/IMPORTS` lines that were no-ops. |

Follow-ups in the same pass: `[Range(1, int.MaxValue)]` on `page` (was 500-ing on
`page=0` via negative SQL `OFFSET`); duplicate detection switched from
locale-dependent message matching to `SqlException.Number is 2601 or 2627`;
currency normalized to uppercase on store; `LoadMore` no longer advances the page
counter on failure; `SupportedCurrencies` split into `All` (validation, retains
HRK for historical rows) and `Selectable` (picker, EUR/USD/GBP/CHF).

**Note on `ROADMAP.md`:** it is not a changelog. Per-commit implementation detail
and bug fixes go here in `AGENTS.md`; `ROADMAP.md` tracks phase deliverables and
its status marks mean "exercised end-to-end" — see its Notes section.

## Reference

`PROJECT.md` is the canonical conventions doc (architecture, code style, response formats, pagination, endpoint patterns). `GetThereAPI/Program.cs` shows the DI wiring and middleware order.
