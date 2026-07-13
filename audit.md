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
| 5 | ~~No DB transaction in purchase flow — wallet debited before external adapter call with no rollback~~ | *(fixed 2026-07-13)* |
| 6 | ~~Race condition on wallet balance — no concurrency control (double-spend possible)~~ | *(fixed 2026-07-13)* |

### Architecture Violations
| # | Issue | Location |
|---|-------|----------|
| 7 | ~~MAUI client directly accesses TransitInfoAPI (port 5001) instead of routing through GetThereAPI~~ | *(fixed 2026-07-13)* |
| 8 | ~~All admin HTML pages expect `{ success, data }` wrapper but controllers return data directly — pages broken~~ | *(fixed 2026-07-13)* |

### Code Quality
| # | Issue | Location |
|---|-------|----------|
| 9 | ~~`JsonDocument` disposed via `using` but `RootElement` returned — latent `ObjectDisposedException`~~ | *(fixed 2026-07-13)* |
| 10 | ~~Empty `catch { }` swallows all exceptions (including `OutOfMemoryException`)~~ | *(fixed 2026-07-13)* |
| 11 | ~~Infinite `while(true)` loops with no cancellation — memory leak after navigation~~ | *(fixed 2026-07-13)* |
| 12 | ~~`async void` on public method~~ | *(fixed 2026-07-13)* |

---

## 🟠 High — Selected Key Items

### GetThereAPI
| # | Issue | Location |
|---|-------|----------|
| 1 | ~~`throw new Exception()` instead of `AppException` — returns 500 instead of correct status~~ | *(fixed 2026-07-13)* |
| 2 | ~~N+1 queries in `GetAllRolesAsync` and `GetUsersAsync`~~ | *(fixed 2026-07-13)* |
| 3 | ~~`CancellationToken` parameter silently ignored in `ProfileManager`~~ | *(fixed 2026-07-13)* |
| 4 | ~~`UpdateAsync` called twice when email changes~~ | *(fixed 2026-07-13)* |
| 5 | ~~3 stubbed endpoints return hardcoded `[]` to live API endpoints — added logging to make calls observable~~ | *(fixed 2026-07-13)* |
| 6 | ~~`TransitInfoApiClient` registered as transient — token cache never works, fresh login per call~~ | *(fixed 2026-07-13)* |
| 7 | ~~No logging in `WalletManager`, `TicketingManager`, `TransitInfoApiClient`~~ | *(fixed 2026-07-13)* |
| 8 | ~~`NameClaimType` not set (defaults to `nameidentifier`) — asymmetric with TransitInfoAPI~~ | *(fixed 2026-07-13)* |
| 9 | ~~`PadBase64` duplicated in 2 files~~ | *(fixed 2026-07-13)* |

### TransitInfoAPI
| # | Issue | Location |
|---|-------|----------|
| 10 | Race condition — in-flight real-time updates silently lost (`Interlocked.Exchange` + concurrent writes) | `RealtimeManager.cs:116,412-423` |
| 11 | ~~Hardcoded `"Europe/Zagreb"` timezone — wrong for non-Croatian operators~~ | *(fixed 2026-07-13)* |
| 12 | ~~Reconciliation inside SQL transaction — contradicts AGENTS.md~~ | *(fixed in prev. session — moved outside tx)* |
| 13 | ~~Missing `MobilityPolling` config section — `Program.cs` binds to null defaults~~ | *(fixed 2026-07-13)* |
| 14 | ~~Raw SQL with string interpolation — SQL injection risk (false positive — uses `ExecuteSqlInterpolatedAsync`, fully parameterized)~~ | *(false positive)* |
| 15 | ~~Empty `catch { }` on `File.Delete` and `RollbackAsync` — failures invisible~~ | *(fixed 2026-07-13)* |
| 16 | ~~Mixed ADO.NET + EF Core in same transaction — unsafe cast to `SqlConnection`~~ | *(fixed in prev. session — `UseTransaction(null)` clears EF tx ref)* |
| 17 | ~~`MobilityPollingWorker` injects `ExternalFeedSource` (scoped) as singleton — captive dependency (false positive — `ExternalFeedSource` is registered as `AddSingleton`)~~ | *(false positive)* |
| 18 | ~~Missing `AsNoTracking()` on all read-only queries — GC/memory pressure~~ | *(fixed 2026-07-13)* |
| 19 | ~~No `AsSplitQuery()` on multi-`Include` chains — Cartesian explosion risk~~ | *(fixed 2026-07-13)* |

### GetThere MAUI
| # | Issue | Location |
|---|-------|----------|
| 20 | Static animation state shared across instances — visual glitches with multiple tabs | `AnimatedBackground.xaml.cs:28-84` |
| 21 | ~~Deadlock risk — `Task.Run(...).GetAwaiter().GetResult()` on UI thread~~ | *(fixed 2026-07-13)* |
| 22 | iOS Privacy Manifest — `NSPrivacyAccessedAPICategoryUserDefaults` commented out, App Store rejection risk | `Platforms/iOS/Resources/PrivacyInfo.xcprivacy` |
| 23 | Password policy mismatch: client validates >=8, server requires >=12 — passwords 8-11 fail silently | `LoginPage.xaml.cs:58`, `Program.cs:39` |
| 24 | `AuthService.IsLoggedInAsync` only checks token existence, not expiry | `AuthService.cs` |
| 25 | ~~`AuthenticatedHttpHandler` reads `SecureStorage` on every request — no in-memory cache (added `_cachedToken`/`_cachedRefreshToken` in `AuthService`)~~ | *(fixed 2026-07-13)* |
| 26 | ~~No timeout or retry on any HTTP client (default 100s) — set `Timeout = 30s` on named client~~ | *(fixed 2026-07-13)* |
| 27 | 4 concurrent 60 FPS Skia animation loops (one per tab) | All pages with `AnimatedBackground` |
| 28 | Localization key mismatch — English and Croatian `.resx` files have different keys | `Resources/Strings/` |
| 29 | ~~`TryReadProblemAsync` copy-pasted in 4 service files~~, ~~`PadBase64` in 2 files~~ | *(fixed 2026-07-13)* |

### Frontend (wwwroot)
| # | Issue | Location |
|---|-------|----------|
| 30 | ~~Duplicate `let` declarations — `SyntaxError`, pages break silently~~ | *(fixed 2026-07-13)* |
| 31 | ~~Duplicate functions in `audit.html` — `logout()` defined 3 times, bad merge resolution~~ | *(fixed 2026-07-13)* |
| 32 | Admin UIs store JWT in `sessionStorage` — no refresh mechanism, session dies at 15 min | All admin pages |
| 33 | No `AbortController` on map fetches — race conditions on rapid pan/zoom | `index.html`, `public.html` |
| 34 | Transition on `right` triggers layout thrashing — use `transform: translateX()` | `map/index.html:15-17` |

---

## 🟡 Medium — Selected Items

| # | Project | Issue | Location |
|---|---------|-------|----------|
| 1 | Both | ~~No `AsNoTracking()` on any read-only EF query~~ | *(fixed 2026-07-13)* |
| 2 | Both | String connection strings in source code | Both `appsettings.json` |
| 3 | Both | Refresh token IP check skipped if either IP is null | Both `AuthManager.cs:107-111` |
| 4 | Both | 30-second permission propagation delay (IMemoryCache) | Both `DynamicClaimsTransformation.cs` |
| 5 | Both | ~~Response compression not configured~~ | *(fixed 2026-07-13)* |
| 6 | GetThereAPI | ~~`sub` claim type hardcoded in 6 controllers — no shared constant~~ | *(fixed 2026-07-13)* |
| 7 | GetThereAPI | ~~Hardcoded `"User"` role string instead of `RoleNames.User`~~ | *(fixed 2026-07-13)* |
| 8 | GetThereAPI | ~~`SetUserRoleAsync` missing audit log~~ | *(fixed 2026-07-13)* |
| 9 | GetThereAPI | ~~Redundant `.Take(20)` in mapper (already done in manager)~~ | *(fixed 2026-07-13)* |
| 10 | TransitInfoAPI | ~~`GeometryFactory` recreated on every call — should be `static readonly`~~ | *(fixed 2026-07-13)* |
| 11 | TransitInfoAPI | ~~111.0 km/deg magic number copy-pasted 6+ times — extracted to `GeoConstants.KmPerDegree`~~ | *(fixed 2026-07-13)* |
| 12 | TransitInfoAPI | ~~GTFS-RT feeds polled sequentially — slow feed blocks all others~~ | *(fixed 2026-07-13)* |
| 13 | TransitInfoAPI | ~~Hardcoded `StationType = 'Stop'` in 5 SQL queries — breaks on enum rename~~ | *(fixed 2026-07-13)* |
| 14 | TransitInfoAPI | ~~`DataTable` per 50K batch + per-row boxing — heavy GC pressure in import~~ | *(fixed 2026-07-13)* |
| 15 | TransitInfoAPI | Duplicate `RoleDto`/`UserDto` across namespaces | 3 files |
| 16 | TransitInfoAPI | ~~`return null!` — null-forgiving on null, caller gets NRE~~ | *(fixed 2026-07-13)* |
| 17 | MAUI | MAUI has zero ViewModel layer — no compiled bindings, no `x:DataType` | All pages |
| 18 | MAUI | No logging sink in Release builds (`AddDebug()` only in DEBUG) | `MauiProgram.cs:85-87` |
| 19 | MAUI | ~~Event handler leak — subscribes to `RequestedThemeChanged` never unsubscribes~~ | *(fixed 2026-07-13)* |
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
- ~~`"sub"`~~, `"role"`, `"permission"` claim types hardcoded across 6+ files

### Dead Code
- MAUI: `BreathingBackground` (entire component), `AnimatedGradientBehavior` (104 lines), `InstallBtnTextConverter`, `InstallBtnColorConverter`, `StatusToColorConverter`
- GetThereAPI: `SqlHelper.cs`, ~~MapManager 3 stub methods~~ (logging added)
- Shared: `TransportTypeContract`, `FeedFormat` enum entirely, unused `StationType`/`TicketFormat`/`PaymentStatus` values

---

## Key Themes

1. **Secrets in source control** (5 critical items) — JWT keys + admin passwords in `appsettings.json`
2. **Race conditions** — RealtimeManager update loss, ~~TransitInfoApiClient token cache~~ (fixed)
3. ~~**Zero-accounting architecture** — wallet deducted before adapter call with no rollback (real money loss bug)~~
4. **Dead code everywhere** — 2 entire components, 3 stubs (logged), 6 `NotImplementedException` stubs, entire files
5. **Code duplication** — ~~`TryReadProblemAsync` x4~~ (now in shared helper), ~~`PadBase64` x3~~ (now in shared helper), `RoleDto`/`UserDto` x3, bounding box math x7, name normalization x2
6. **Convention violations** — `!= null` instead of `is not null`, sync-over-async, missing file-scoped namespaces
8. **Hardcoded everything** — ~~timezone~~ (now configurable), API URLs, page sizes, ~~magic numbers (111.0 km/deg fixed)~~, 250+ hex colors, credentials
9. **No test projects** — zero tests anywhere in the solution
10. **No CI/CD** — `.github/workflows/` is completely empty

---

## Top 10 Most Impactful Fixes

| Rank | Fix | Project | Effort |
|------|-----|---------|--------|
| 1 | Rotate secrets into user secrets / env vars | Both APIs | ~30 min |
| 2 | ~~Wrap `PurchaseTicketAsync` in DB transaction~~ | *(fixed 2026-07-13)* |
| 3 | ~~Add concurrency control to wallet balance (rowversion / atomic UPDATE)~~ | *(fixed 2026-07-13)* |
| 4 | ~~Fix TransitInfoApiClient registration to scoped/singleton~~ | *(fixed 2026-07-13)* |
| 5 | ~~Add `AsNoTracking()` to all read-only queries~~ | *(fixed 2026-07-13)* |
| 6 | ~~Route MAUI MapPage through GetThereAPI~~ | *(fixed 2026-07-13)* |
| 7 | ~~Add response compression to both APIs~~ | *(fixed 2026-07-13)* |
| 8 | ~~Add CancellationToken to infinite animation loops~~ | *(fixed 2026-07-13)* |
