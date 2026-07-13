# Code Audit Report — GetThere Solution
**Date:** 2026-07-13 | **Scope:** ~200 files across 4 projects

## Executive Summary

| Severity | Count |
|----------|-------|
| Critical | ~12 |
| High | ~35 |
| Medium | ~65 |
| Low | ~390+ |
| **Total** | **~500+** |

---

## 🔴 Critical

### Secrets in Source Control
| # | Issue | Location |
|---|-------|----------|
| 1 | JWT signing key (can forge tokens) | `GetThereAPI/appsettings.json:7`, `TransitInfoAPI/appsettings.json:38` |
| 2 | Admin passwords in plaintext | `GetThereAPI/appsettings.json:28`, `TransitInfoAPI/appsettings.json:55` |
| 3 | Service account credentials in plaintext | Both `appsettings.json` files |
| 4 | No `.gitignore` protection for secrets | `.gitignore` |

### Data Integrity / Money Loss
| # | Issue | Location |
|---|-------|----------|
| 5 | No DB transaction in purchase flow — wallet debited before external adapter call with no rollback | `TicketingManager.cs:59-137` |
| 6 | Race condition on wallet balance — no concurrency control (double-spend possible) | `WalletManager.cs`, `TicketingManager.cs` |

### Architecture Violations
| # | Issue | Location |
|---|-------|----------|
| 7 | MAUI client directly accesses TransitInfoAPI (port 5001) instead of routing through GetThereAPI | `MapPage.xaml.cs:15-18` |
| 8 | All admin HTML pages expect `{ success, data }` wrapper but controllers return data directly — pages broken | `GetThereAPI/wwwroot/admin/*.html` |

### Code Quality
| # | Issue | Location |
|---|-------|----------|
| 9 | `JsonDocument` disposed via `using` but `RootElement` returned — latent `ObjectDisposedException` | `MapManager.cs:98-102` |
| 10 | Empty `catch { }` swallows all exceptions (including `OutOfMemoryException`) | 15+ locations across all projects |
| 11 | Infinite `while(true)` loops with no cancellation — memory leak after navigation | `AnimatedBackground.xaml.cs`, `BreathingBackground.xaml.cs` |
| 12 | `async void` on public method | `App.xaml.cs:55` |

---

## 🟠 High — Selected Key Items

### GetThereAPI
| # | Issue | Location |
|---|-------|----------|
| 1 | `throw new Exception()` instead of `AppException` — returns 500 instead of correct status | `RolePermissionManager.cs:54,76,106` |
| 2 | N+1 queries in `GetAllRolesAsync` and `GetUsersAsync` | `RolePermissionManager.cs:32-37,127-141` |
| 3 | `CancellationToken` parameter silently ignored in `ProfileManager` | `ProfileManager.cs:17` |
| 4 | `UpdateAsync` called twice when email changes | `ProfileManager.cs:27-34` |
| 5 | 3 stubbed endpoints return hardcoded `[]` to live API endpoints | `MapManager.cs:86-102` |
| 6 | `TransitInfoApiClient` registered as transient — token cache never works, fresh login per call | `Program.cs:31-35` |
| 7 | No logging in `WalletManager`, `TicketingManager`, `TransitInfoApiClient` | 3 entire classes |
| 8 | `NameClaimType` not set (defaults to `nameidentifier`) — asymmetric with TransitInfoAPI | `Program.cs:67-80` |
| 9 | `PadBase64` duplicated in 2 files | `AuthenticatedHttpHandler.cs`, `AuthService.cs` |

### TransitInfoAPI
| # | Issue | Location |
|---|-------|----------|
| 10 | Race condition — in-flight real-time updates silently lost (`Interlocked.Exchange` + concurrent writes) | `RealtimeManager.cs:116,412-423` |
| 11 | Hardcoded `"Europe/Zagreb"` timezone — wrong for non-Croatian operators | `ScheduleManager.cs:13` |
| 12 | Reconciliation inside SQL transaction — contradicts AGENTS.md | `FeedManager.cs:388` |
| 13 | Missing `MobilityPolling` config section — `Program.cs` binds to null defaults | `appsettings.json` |
| 14 | Raw SQL with string interpolation — SQL injection risk | `FeedManager.cs:1101-1102` |
| 15 | Empty `catch { }` on `File.Delete` and `RollbackAsync` — failures invisible | `FeedManager.cs:774,1206,1224` |
| 16 | Mixed ADO.NET + EF Core in same transaction — unsafe cast to `SqlConnection` | `FeedManager.cs:355-391` |
| 17 | `MobilityPollingWorker` injects `ExternalFeedSource` (scoped) as singleton — captive dependency | `MobilityPollingWorker.cs` |
| 18 | Missing `AsNoTracking()` on all read-only queries — GC/memory pressure | All managers |
| 19 | No `AsSplitQuery()` on multi-`Include` chains — Cartesian explosion risk | `ReconciliationManager.cs`, `OperatorManager.cs` |

### GetThere MAUI
| # | Issue | Location |
|---|-------|----------|
| 20 | Static animation state shared across instances — visual glitches with multiple tabs | `AnimatedBackground.xaml.cs:28-84` |
| 21 | Deadlock risk — `Task.Run(...).GetAwaiter().GetResult()` on UI thread | `App.xaml.cs:24-25` |
| 22 | iOS Privacy Manifest — `NSPrivacyAccessedAPICategoryUserDefaults` commented out, App Store rejection risk | `Platforms/iOS/Resources/PrivacyInfo.xcprivacy` |
| 23 | Password policy mismatch: client validates >=8, server requires >=12 — passwords 8-11 fail silently | `LoginPage.xaml.cs:58`, `Program.cs:39` |
| 24 | `AuthService.IsLoggedInAsync` only checks token existence, not expiry | `AuthService.cs` |
| 25 | `AuthenticatedHttpHandler` reads `SecureStorage` on every request — no in-memory cache | `AuthenticatedHttpHandler.cs` |
| 26 | No timeout or retry on any HTTP client (default 100s) | `MauiProgram.cs:49-51` |
| 27 | 4 concurrent 60 FPS Skia animation loops (one per tab) | All pages with `AnimatedBackground` |
| 28 | Localization key mismatch — English and Croatian `.resx` files have different keys | `Resources/Strings/` |
| 29 | `TryReadProblemAsync` copy-pasted in 4 service files, `PadBase64` in 2 files | Multiple service files |

### Frontend (wwwroot)
| # | Issue | Location |
|---|-------|----------|
| 30 | Duplicate `let` declarations — `SyntaxError`, pages break silently | `users.html:31,112`, `tickets.html:33,131` |
| 31 | Duplicate functions in `audit.html` — `logout()` defined 3 times, bad merge resolution | `audit.html` |
| 32 | Admin UIs store JWT in `sessionStorage` — no refresh mechanism, session dies at 15 min | All admin pages |
| 33 | No `AbortController` on map fetches — race conditions on rapid pan/zoom | `index.html`, `public.html` |
| 34 | Transition on `right` triggers layout thrashing — use `transform: translateX()` | `map/index.html:15-17` |

---

## 🟡 Medium — Selected Items

| # | Project | Issue | Location |
|---|---------|-------|----------|
| 1 | Both | No `AsNoTracking()` on any read-only EF query | All managers |
| 2 | Both | String connection strings in source code | Both `appsettings.json` |
| 3 | Both | Refresh token IP check skipped if either IP is null | Both `AuthManager.cs:107-111` |
| 4 | Both | 30-second permission propagation delay (IMemoryCache) | Both `DynamicClaimsTransformation.cs` |
| 5 | Both | Response compression not configured | Both `Program.cs` |
| 6 | GetThereAPI | `sub` claim type hardcoded in 6 controllers — no shared constant | Multiple controllers |
| 7 | GetThereAPI | Hardcoded `"User"` role string instead of `RoleNames.User` | `AuthManager.cs:47` |
| 8 | GetThereAPI | `GetUserRole` missing audit log | `RolePermissionManager.cs:144-155` |
| 9 | GetThereAPI | Redundant `.Take(20)` in mapper (already done in manager) | `WalletMapper.cs:14` |
| 10 | TransitInfoAPI | `GeometryFactory` recreated on every call — should be `static readonly` | `RouteManager.cs:137`, `FeedManager.cs:607` |
| 11 | TransitInfoAPI | 111.0 km/deg magic number copy-pasted 6+ times | 5 managers |
| 12 | TransitInfoAPI | GTFS-RT feeds polled sequentially — slow feed blocks all others | `RealtimeManager.cs:71-113` |
| 13 | TransitInfoAPI | Hardcoded `StationType = 'Stop'` in 5 SQL queries — breaks on enum rename | `FeedManager.cs:1194` |
| 14 | TransitInfoAPI | `DataTable` per 50K batch + per-row boxing — heavy GC pressure in import | `FeedManager.cs:987-1032` |
| 15 | TransitInfoAPI | Duplicate `RoleDto`/`UserDto` across namespaces | 3 files |
| 16 | TransitInfoAPI | `return null!` — null-forgiving on null, caller gets NRE | `StationManager.cs:219` |
| 17 | MAUI | MAUI has zero ViewModel layer — no compiled bindings, no `x:DataType` | All pages |
| 18 | MAUI | No logging sink in Release builds (`AddDebug()` only in DEBUG) | `MauiProgram.cs:85-87` |
| 19 | MAUI | Event handler leak — subscribes to `RequestedThemeChanged` never unsubscribes | `LoginPage.xaml.cs:20` |
| 20 | MAUI | Profile page builds 200+ countries programmatically (~1000 objects) instead of `CollectionView` | `ProfilePage.xaml.cs:323-424` |
| 21 | MAUI | 5 `<Path>` elements with no `Data` attribute — invisible | `BreathingBackground.xaml:6-10` |
| 22 | Shared | `TransportTypeResponse` is dead code — never returned by any controller | `TransportTypeContract.cs` |
| 23 | Shared | `WalletContract.FormattedAmount` hardcodes `€` symbol, ignores `Currency` property | `WalletContract.cs:23` |
| 24 | Shared | Missing validation attributes on `Amount`, `AdapterId`, `OptionId` | `WalletContract.cs`, `TicketContract.cs` |
| 25 | Shared | `PagedResult<T>` vs `Paginated<T>` naming mismatch with PROJECT.md | `PROJECT.md` vs `PagedResult.cs` |

---

## 🟢 Low — Highlights

### Magic Numbers (165+ total)
- **GetThereAPI:** 10 (page sizes, timeouts, lockout settings, rate limits)
- **TransitInfoAPI:** 33 (grid cell sizes, batch sizes, timeouts, 4326 SRID)
- **MAUI:** 120+ in XAML (sizes, margins, radii, font sizes, animation params, opacity)

### Hardcoded Colors (250+ hex values)
- All MAUI XAML pages bypass `Colors.xaml` resource dictionary: `#512BD4`, `#10B981`, `#059669`, `#009688`, `#111827`, etc.

### Hardcoded Strings (370+ total)
- API URLs in MAUI must recompile to change
- Route strings in 4 service files (12 total)
- Culture codes `"hr-HR"` and `"en-US"` duplicated
- `"sub"`, `"role"`, `"permission"` claim types hardcoded across 6+ files

### Dead Code
- MAUI: `BreathingBackground` (entire component), `AnimatedGradientBehavior` (104 lines), `InstallBtnTextConverter`, `InstallBtnColorConverter`, `StatusToColorConverter`
- GetThereAPI: `SqlHelper.cs`, `MapManager` 3 stub methods
- Shared: `TransportTypeContract`, `FeedFormat` enum entirely, unused `StationType`/`TicketFormat`/`PaymentStatus` values

---

## Key Themes

1. **Secrets in source control** (5 critical items) — JWT keys + admin passwords in `appsettings.json`
2. **Race conditions** — RealtimeManager update loss, wallet balance double-spend, TransitInfoApiClient token cache
3. **Zero-accounting architecture** — wallet deducted before adapter call with no rollback (real money loss bug)
4. **Dead code everywhere** — 2 entire components, 3 stub methods, 6 `NotImplementedException` stubs, entire files
5. **Code duplication** — `TryReadProblemAsync` x4, `PadBase64` x3, `RoleDto`/`UserDto` x3, bounding box math x7, name normalization x2
6. **Convention violations** — `!= null` instead of `is not null`, sync-over-async, missing file-scoped namespaces
8. **Hardcoded everything** — timezone, API URLs, page sizes, magic numbers, 250+ hex colors, credentials
9. **No test projects** — zero tests anywhere in the solution
10. **No CI/CD** — `.github/workflows/` is completely empty

---

## Top 10 Most Impactful Fixes

| Rank | Fix | Project | Effort |
|------|-----|---------|--------|
| 1 | Rotate secrets into user secrets / env vars | Both APIs | ~30 min |
| 2 | Wrap `PurchaseTicketAsync` in DB transaction | GetThereAPI | ~2 lines |
| 3 | Add concurrency control to wallet balance (rowversion / atomic UPDATE) | GetThereAPI | ~3 lines |
| 4 | Fix TransitInfoApiClient registration to scoped/singleton | GetThereAPI | ~3 lines |
| 5 | Add `AsNoTracking()` to all read-only queries | Both APIs | ~30 edits |
| 6 | Fix admin HTML pages to expect raw data (or wrap in OperationResult) | GetThereAPI | ~1 edit per page |
| 7 | Route MAUI MapPage through GetThereAPI | MAUI | ~5 lines |
| 8 | Add response compression to both APIs | Both APIs | ~5 lines |
| 9 | Remove duplicate `let` declarations and functions from admin HTML | Frontend | ~5 edits |
| 10 | Add CancellationToken to infinite animation loops | MAUI | ~5 lines |
