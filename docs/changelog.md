# Change Log

Per-session implementation detail: what changed, in which files, and why.

This lived in `AGENTS.md`, which is read as context at the start of every session. Two thirds of
that file had become a changelog nobody needed in order to work in the repo, so it is here instead.
`AGENTS.md` keeps the conventions and the standing rules; `ROADMAP.md` tracks phase deliverables.

Newest last.

---

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

## Session — July 26, 2026 (later)

### Applied — Claude Design import ("GetThere UI", project `b1dc673d-8bf5-45c9-9e07-84e8f0645e41`)

The design project holds three canvases: `GetThereAPI Admin.dc.html` (1a overview,
1b adapters), `TransitInfoAPI Admin.dc.html` (2a data health, 2b reconciliation
review) and `GetThere App.dc.html` (3a–3h MAUI screens). `support.js` is the
design-canvas runtime and is not part of the app.

| Area | File(s) | What |
|------|---------|------|
| Icons | `GetThereAPI/wwwroot/admin/icons/`, `TransitInfoAPI/wwwroot/admin/icons/` | The 10 design icons, copied from `GetThere/Resources/Images` — the repo copies keep their `<style>` blocks, so they mask correctly; the design-project copies had them stripped |
| Design system | `GetThereAPI/wwwroot/admin/style.css` | Full token set + shell/rail/topbar/KPI/card/table/badge/pill/button/field/code primitives. Violet accent |
| Design system | `TransitInfoAPI/wwwroot/admin/style.css` | Same file, teal accent block, plus a `body.bs` Bootstrap 5 dark skin |
| Routing fix | `GetThereAPI/Controllers/AdminController.cs` | `SetRoleRequest` sat between `[ApiController]/[Route]/[Authorize]` and the class, so the attributes bound to the **record** — `/admin/users` and `/admin/audit` were actually served at `/users` and `/audit` and the admin pages 404'd |
| Audit page fix | `GetThereAPI/wwwroot/admin/audit.html` | Was parsing `j.success` / `j.data.items`; `PagedResult<T>` has no envelope, so the page never rendered |
| New endpoints | `AdminContract.cs`, `AdminManager.cs`, `AdminController.cs` | `GET /admin/stats` (KPIs over a rolling window vs the previous one), `GET /admin/purchases` (feed, filter by status/adapter), `GET /admin/adapters` (health incl. `AdapterRegistry` registration), `PATCH /admin/adapters/{id}` (enable/disable, audit-logged) |
| Admin shell | `GetThereAPI/wwwroot/admin/admin.js` | Rail/topbar renderer + auth, fetch, formatting helpers. Avatar initials come from the JWT; the rail's TransitInfoAPI dot probes `/api/map/transport-types` |
| Screens 1a/1b | `index.html`, `adapters.html` | Overview (KPIs, purchase feed, adapter health, needs-attention) and adapter detail (metrics, `ITicketingAdapter` contract cards, purchase tail, binding/credentials/config) |
| Restyle | `users.html`, `tickets.html`, `audit.html`, `login.html` | Moved onto the shell; Bootstrap CDN dropped from GetThereAPI admin |
| Screen 2a | `TransitInfoAPI/wwwroot/admin/index.html`, `admin-shell.js` | Data-health overview: station/route counts, feed health bars, live vehicles, review queue, feed table with filters, reconciliation queue, service alerts |
| Login | `TransitInfoAPI/wwwroot/admin/login.html` | Restyled; `redirect_after_login` compared a full URL against `'/admin/'` so it always fell back — now parses the URL and keeps the same-origin guard |
| Legacy pages | 14 × `TransitInfoAPI/wwwroot/admin/*.html` | `<body class="bs">` so they inherit the dark palette without touching their logic |
| MAUI tokens | `Resources/Styles/Colors.xaml` | Design tokens named after the CSS custom properties (text ramp, teal/violet accents, status surfaces, card gradient brush) |
| MAUI 3e/3h | `Helpers/PageUtility.cs`, `TicketsViewModel.cs`, `TicketsPage.xaml` | `TicketStatusColorConverter` (Used/Expired no longer render with the Active green badge) and `FilterChipConverter` + `ActiveFilterKey` (the selected filter chip now highlights) |

**Not done:** screen 2b (reconciliation review master-detail). `reconciliation.html`
carries search, route-type/status filters, tabs, batch approve and per-item
approve/reject-with-warning; re-laying it out is a rewrite of that logic, so it was
left working and dark-skinned instead. The remaining MAUI screens (3a–3d, 3f–3g)
already matched the mockups — the canvases were drawn from the existing app — so
only the token extraction above was needed.

## Session — July 27, 2026

### Full-solution audit + remediation (fresh read of `main` @ `773b694`)

Audit findings live in `plans/` (see the session plan); the money path was **reported, not
modified** — see `docs/money-path-defects.md`.

| Area | File(s) | What |
|------|---------|------|
| **Broken access control** | `PermissionKeys.cs`, `AdminController.cs`, `Program.cs` | `/admin/stats` and `/admin/purchases` were gated on `tickets.view`, which the **User** role holds — any account could read every user's purchases (incl. email) and the wallet float. New admin-only `AdminStatsView`/`AdminPurchasesView`. The User-role grant list moved out of `Program.cs` into `PermissionKeys.UserRoleDefaults` so it is testable |
| **Refresh-token reuse** | both `AuthManager.cs` | Reuse detection was unreachable: rotation sets `RevokedAt` *and* `ReplacedByToken`, so a replayed token failed the `IsActive` guard first and the family-revocation branch never ran. Reordered; reuse is now audit-logged |
| Claims cache | both `DynamicClaimsTransformation.cs` | `SlidingExpiration` alone never lapses for an active user, so a revoked role could stay live indefinitely. Added a 5-minute absolute ceiling |
| Admin console | both `Program.cs` | The `/admin` guard 401'd every browser navigation (no bearer header on a navigation), including `login.html` — the consoles were unreachable. Removed; authorization stays per-endpoint on the API, pages get `X-Robots-Tag: noindex` |
| Middleware order | `GetThereAPI/Program.cs` | `UseHttpsRedirection` ran after auth/static/`/health`; added HSTS, `UseForwardedHeaders` (rate limiting partitioned on the proxy IP), and scoped the `AllowAnyOrigin` CORS policy to `/map` instead of globally |
| Seeded credentials | both `Program.cs` | Admin + service-account passwords were generated and written to plaintext files on every cold start. Now `Seed:AdminPassword` / `Seed:ServiceAccountPassword` outside Development; the file is Development-only |
| Path traversal | `FeedContract.cs`, `FeedManager.cs` | `FeedId` (unrestricted, caller-supplied) flowed into `Path.Combine(...,"feeds",FeedId)`. Character-restricted in the contract; all three call sites go through `GetFeedStorageDirectory`, which verifies the resolved path stays under `feeds/` |
| SSRF + bombs | `ExternalFeedSource.cs`, `FeedManager.cs` | Feed URLs are fetched server-side with no destination check. Added a private/loopback/link-local blocklist (incl. `169.254.169.254`), re-checked after redirects, a 512 MB streamed download cap, and a 4 GB declared-expansion guard on the archive. `Feeds:AllowPrivateNetworkUrls` is the dev escape hatch |
| XSS | 14 × `TransitInfoAPI/wwwroot/admin/*.html` | Each page had its own `esc()` built on `textContent`, which does **not** escape `"` or `'` — and it was used inside `title="…"`, `value="…"` and `href="…"`. Replaced with a quote-escaping version; added `safeUrl()` so a feed/operator URL cannot be `javascript:` |
| **Reconciliation matching** | `ReconciliationManager.cs` | Candidate lookup read only the grid cell containing the raw stop, so stations metres apart across a ~22 km cell boundary were never compared — silent duplicate `CanonicalStation` rows. Now scans the 3×3 neighbourhood, like `PlaceMatchingManager` already did |
| Vehicle bounding box | `TransitInfoApiClient.cs` | `minLat = lat; maxLat = lat + r` built a box extending only north-east — vehicles south/west of the caller were never returned. Now centred; uses `GeoConstants.KmPerDegree` |
| Culture bugs | `TransitInfoApiClient.cs`, `LocalizationService.cs` | Coordinates were interpolated with the current culture (`lat=45,8` under hr-HR); now invariant. `SetCulture` set only `Thread.CurrentThread`, so language changes half-applied across continuations |
| MAUI auth | `AuthService.cs`, `MauiProgram.cs` | `AuthService` was **transient** and new'd its own `HttpClient`, so the token cache was per-instance and useless. Now singleton, with refresh serialized behind a `SemaphoreSlim` — concurrent requests each rotated the refresh token and the loser was signed out |
| Upstream errors | `TransitInfoApiClient.cs` | `EnsureSuccessStatusCode()` surfaced upstream failures as bare 500s; now 502 `AppException`. Static `JsonSerializerOptions`; token invalidation moved under the semaphore; the empty `catch {}` around token-expiry parsing now logs and falls back to 10 min instead of 1 h |
| Worker | `TicketExpiryWorker.cs` | Slept before its first sweep (nothing expired for an hour after restart) and a configured `0` would have spun against the DB. Sweeps first; interval floored at 1 min |
| Perf | `AdminManager.cs` | `GetStatsAsync` materialised every pending purchase to call `Count()`/`Min()`; now aggregates in SQL |
| Dead code | 6 managers | Removed injected-but-unused dependencies (`MapManager._db`, `FeedManager._httpFactory`, `RealtimeManager._httpFactory`, `MobilityManager._config`, `RouteManager._config`) |
| **Migration drift** | `20260727081211_...` | Scaffolding re-emitted the whole `HardenImportedTickets` DDL — the model snapshot had drifted from the migration history, so the *next* migration anyone generated would have failed against a migrated DB. Migration trimmed to the new unique index on `RefreshTokens.Token`; the regenerated snapshot repairs the drift |
| **Tests** | `tests/GetThere.Tests/` | First test project in the solution. 36 tests: endpoint/permission matrix (fails if an admin endpoint is gated on a User-role permission), reconciliation cell-boundary regressions, SSRF blocklist, `PagedResult<T>` serialization contract |
| CI | `build-check.yml` | Added the test job, a blocking `--vulnerable` scan, `permissions:`, NuGet caching, a concurrency group, and `develop` to the PR trigger |

**Still open (deliberately):** the money path (`docs/money-path-defects.md`) and the map boundary
violation (`docs/map-proxy-migration.md`). `audit.md` items #7 and #10 were marked fixed but are
not — corrected in place.

### Second pass — medium findings, analyzers, and runtime verification

| Area | File(s) | What |
|------|---------|------|
| **Refresh-token queries threw at runtime** | both `AuthManager.cs` | `.Where(rt => ... && rt.IsActive)` — `IsActive` is a computed, unmapped property, so EF cannot translate it and the query throws. This made `POST /auth/change-password` return 500, and it would have broken the reuse-detection branch the moment that branch became reachable. Replaced with `RevokedAt == null && ExpiresAt > now` at all four sites |
| **`IX_RefreshTokens_Token` never existed** | `AppDbContext.cs`, `20260727092919_HardenRefreshTokenIndex` | The model declared the index from `AddIdentity` onwards, but `Token` was unbounded → `nvarchar(max)`, which SQL Server cannot index. Every refresh was a table scan. Column bounded to 128 and the index created as unique; migration applied and verified |
| Realtime resilience | `RealtimeManager.cs` | The cache was rebuilt from each cycle's successes alone, so one feed failing blanked that operator's realtime data until the next good poll. Results are now held per feed and flattened |
| Admin sessions | `admin.js`, `admin-shell.js`, `TransitInfoAPI/.../login.html` | Both consoles dropped the operator at the 15-minute access-token expiry. Added a shared-in-flight refresh with a single retry; TransitInfoAPI's login was not storing the refresh token at all |
| IP binding | both `AuthManager.cs` | The check was skipped when *either* address was null, so suppressing the caller address bypassed it entirely |
| Money formatting | `MoneyFormatter.cs`, `WalletContract.cs`, `ProfileViewModel.cs` | Was hardcoded `€` in one place and a fixed `hr-HR` culture in another. One currency-aware formatter now |
| Perf | `PlaceMatchingManager.cs`, `ReconciliationManager.cs` | `AsNoTracking` on the whole-table place cache; Levenshtein rewritten from an m×n matrix to two rolling rows (verified equivalent against the old implementation over 300 random pairs) |
| Analyzers | `Directory.Build.props`, `.editorconfig` | `latest-recommended` enabled. Noisy logging/style rules turned off with reasons; the culture rules stay strict server-side and are off in the MAUI client, where current-culture display formatting is correct. ~390 initial warnings resolved to zero |
| Packages | `Directory.Packages.props` | Central package management; versions were duplicated across four csproj files |
| MAUI CI | `build-check.yml` | 76 `CS0618` warnings (obsolete `DisplayAlert`/`DisplayActionSheet`) cleared, so the MAUI job is no longer `continue-on-error` and builds with `-warnaserror` |

**Verified against a running API:** a plain user now gets 403 on `/admin/purchases`, `/admin/stats`,
`/admin/users` and `/admin/audit` (was 200 with every user's email); admin still gets through; the
admin console and its login page load without a bearer token; replaying a rotated refresh token
returns 401 *and* revokes the whole family; change-password succeeds and the new password works.

**`GetThereDB` was not built from these migrations** — see `docs/database-drift.md`. Dropped and
rebuilt on request; all four previously-500 endpoints now return 200.

### Third pass — money path, database rebuild, remaining cleanups

The money path was off-limits for the first two passes and was then explicitly authorised.

| Area | File(s) | What |
|------|---------|------|
| **Purchase flow rewritten** | `TicketingManager.cs` | Three stages: validate (adapter registered, option, wallet, currency, idempotency) → debit + `Pending` purchase committed → adapter called **with no transaction open** → settle or reverse. A failed purchase now writes a compensating `Refund` and restores the balance instead of committing the debit and throwing. Full detail in `docs/money-path-defects.md` |
| **Adapter checked before the debit** | `TicketingManager.cs` | With no `ITicketingAdapter` registered — the state the app ships in — every purchase used to charge and fail. Now 503 with no money moved |
| Idempotency | `Purchase.cs`, `AppDbContext.cs`, `TicketingController.cs` | `Idempotency-Key` header; filtered unique index per user; a retry replays the original ticket |
| Currency | `TicketingManager.cs` | A wallet in a different currency to the option is rejected rather than debited at face value |
| Top-up | `WalletManager.cs`, `WalletController.cs`, `PermissionKeys.cs` | New admin-only `wallets.topup` (it still credits without taking payment); 1000 cap, 2-dp and payment-method validation, audit-logged; returns the balance *after* the credit |
| Ticket expiry | `TicketExpiryWorker.cs` | Now expires purchased `Tickets`, not just `ImportedTickets` |
| **Mapper NRE** | `TicketingManager.cs` | `TicketMapper.ToTicketResponse` dereferences `Purchase.TicketOption`, which is never populated (the option is read `AsNoTracking`) — every *successful* purchase would have thrown. Invisible until purchases could succeed. Ticket is re-read with its navigations |
| Registration enumeration | `AuthManager.cs` | A duplicate address returned 409 `EMAIL_ALREADY_IN_USE`, making registration an account oracle. Now indistinguishable from success, audit-logged, with the Identity duplicate-error race collapsed the same way |
| Fallback country | `PlaceMatchingManager.cs`, `appsettings.json` | `DefaultCountryId` was a raw identity that defaulted to `1` in code while config said `2`. Replaced with `DefaultCountryIsoCode` resolved by ISO code |
| Magic numbers | `FeedManager.cs`, `ReconciliationManager.cs` | `SetCommandTimeout(600)` → `FeedImportOptions.BulkCommandTimeoutSeconds`; `autoDistThreshold * 2` → named `CandidateSearchRadiusFactor` |
| Animation state | `AnimatedBackground.xaml.cs` | Position and velocity were `static`, so every instance shared them and tabs fought over the same blobs; `_initialized` was never set, so each construction reset every other instance's velocity |
| Admin consoles | both `Program.cs` | CSP, `X-Content-Type-Options` and `Referrer-Policy` on `/admin` |
| Dead code | `GetThereShared`, `.resx` | Deleted `TransportTypeContract`; removed the two unused `PasswordTooShort` keys from both resource files |

**Tests: 60** (was 0 before this audit). `tests/GetThere.Tests/Money/` runs nine of them against a
real SQL Server database — `EnsureDeleted` + `Migrate` per run — because the debit path depends on
raw SQL, transactions and a filtered unique index that the in-memory provider does not implement.

### Fourth pass — map proxy, and TransitInfoAPI made startable

| Area | File(s) | What |
|------|---------|------|
| **TransitInfoAPI could not start** | `docs/transitinfodb-rebaseline.md` | Startup `MigrateAsync` died on `There is already an object named 'Countries'`. The migrations folder was squashed to `20260722145915_InitialCreate` but the database still held the pre-squash 35-migration history. Repaired **without data loss**: created the nine Identity/auth tables the squash introduced (extracted from `dotnet ef migrations script`, applied in one transaction) and stamped the baseline. 4.2M StopTimes intact; `/health` 200 |
| **H5 — map through the proxy** | `MapProxyController.cs`, `MapManager.cs`, `TransitInfoApiClient.cs`, `public.html`, `MapPage.xaml(.cs)`, `ApiEndpoints.cs` | `GET /api/map/upstream/{**path}` forwards whitelisted reads verbatim, so the page gets GeoJSON without GetThereAPI re-modelling it. The allowlist is the security control — the proxy authenticates as the service account, so an open path would expose TransitInfoAPI's admin surface to anyone with `map.view`. `ApiEndpoints.TransitInfoApiBase` deleted: the client no longer knows TransitInfoAPI exists |
| Token into the WebView | `MapPage.xaml.cs`, `public.html` | The page queues requests until `window.setAuthToken(...)` arrives after navigation. Not passed in the URL, which would put it in request logs and history |
| Three stubs implemented | `MapManager.cs` | `GetDeparturesAsync`, `GetStationOperatorsAsync`, `GetTransportTypesAsync` returned hardcoded `[]` (`audit.md` high #5). They call upstream now |
| Upstream errors | `TransitInfoApiClient.cs` | A connection failure or timeout to TransitInfoAPI escaped as `HttpRequestException` → bare 500. Now 502 |

**Verified with both APIs live against real data:** stations 200 (133 KB `FeatureCollection`),
vehicles 200 (114 KB), mobility 200 (23 KB); `operators`, `feeds`, `users`,
`reconciliation/candidates`, `agencies` and traversal attempts all 404 and never forwarded; no token
→ 401.

**Verified end-to-end after the rebuild:** duplicate registration is indistinguishable from success;
`/wallet/ensure` → 201 then `/wallet` → 200; `/tickets`, `/tickets/options`, `/admin/stats`,
`/admin/purchases`, `/admin/adapters`, `/admin/users` all 200; `/wallet/topup` → 403 for a plain user.


---

## Session — July 30, 2026

### Audit pass — GetThereAPI, MAUI client, SharedAuth, both `wwwroot` front-ends

Report-only pass; **no code changed**, because nothing in that container could be compiled or run.
The report itself (`audit-2026-07-30.md`) has since been deleted along with the other audit files —
what it found that still matters is either fixed below or recorded in `docs/money-path-defects.md`.

| Area | What |
|------|------|
| **Why report-only** | No .NET SDK, and installing one is blocked by egress policy: `builds.dotnet.microsoft.com:443` answers 403 to CONNECT at the agent proxy. No SQL Server either, so no migration and no live feed. The two previous audits both had a build behind them; this one is a static read and says so at the top |
| **Scope** | Full read of `SharedAuth`, of everything `GetThereAPI` does with money/identity/uploads/the cross-API boundary, and of both map front-ends. Admin JS and the MAUI ViewModels were swept for defect classes rather than read line-by-line — the report's *Coverage* table states exactly which is which |
| **Found** | 1 High (the refund path's double-credit guard is a read-then-write race with no unique index behind it, and the reconciliation worker has no leader election), 8 Medium, 9 Low, 3 documentation |
| **Carried-forward** | Every `H*`/`M*`/`L*` from `audit-2026-07-28.md` re-derived: 11 fixed, 3 half-fixed, 4 still open. Its "Still open" section was stale in the direction that causes rework, so both older audits now carry status banners |
| **No regression** | The 28 findings in `audit-transitinfo-2026-07-29.md` were checked against the current tree; none has regressed on the surfaces this pass read |
| **Still never audited** | `tests/GetThere.Tests` — excluded on 07-28 and again here. 3 748 lines and CI's only correctness gate |

---

## Session — July 31, 2026

### Map UI moved into the page; client reads TransitInfoAPI directly

The map screen was a MapLibre page in a WebView with its chrome drawn natively over it. Every
control existed twice — once in XAML, once in JavaScript — and `MapPage` drove the page through
four `EvaluateJavaScriptAsync` bridges. Moving the chrome into the page removed that duplication;
moving the page to TransitInfoAPI removed the machinery around it, because the page is then
same-origin with the data it reads.

| Area | File(s) | What |
|------|---------|------|
| **Chrome into the page** | `TransitInfoAPI/wwwroot/map/public.{html,js}` | Search field, transport-mode chips, recentre and layers, wired with `addEventListener` (the page's CSP has no `'unsafe-inline'` in `script-src`). Chips open on Tram and turning the last one off restores everything, as the view model did. Safe-area insets and `viewport-fit=cover`, which MAUI used to handle |
| **Search made real** | same | The field had been bound to `MapViewModel.SearchText` and read by nothing since it was added. Debounced, floored at two characters, `AbortController` on the in-flight request; picking a result flies the map in and reuses the page's existing `showStationDetails` — a `StationResponse` already carries the properties it expects |
| **Localisation** | same, `MapViewModel.cs`, `ApiEndpoints.cs` | The page carries an en/hr table keyed off `?lang=`; the client passes its current culture. `LoadMap` runs from `OnAppearing`, so returning to the tab after a language change reloads the page in it |
| **Client points at TransitInfoAPI** | `ApiEndpoints.cs`, `Resources/Raw/appsettings.json` | Second configured address, `Map:BaseUrl`, defaulting to the **https** profile (5001) — the Android manifest sets `usesCleartextTraffic="false"`, so the http profile fails silently on device |
| **`MapPage` reduced to a WebView** | `MapPage.xaml(.cs)`, `MapViewModel.cs` | Four JS bridges, the token handshake, `MapModeChip`, `ToggleModeCommand`, `ModeFilterChanged` and `SearchText` all deleted. `DsMapControl` and ten `Map_*` resx keys went with them |
| **Two endpoints opened** | `RealtimeController.cs`, `StationsController.cs` | `realtime/vehicles` and `stations/search` were gated behind permissions the service account held on the page's behalf. With no proxy there is no service account, so both are `[AllowAnonymous]` like the four map endpoints beside them. Public transit facts; the rate limiter already partitions anonymous callers by address |
| **GetThereAPI's map path retired** | `MapProxyController.cs`, `MapManager.cs`, `Program.cs`, `wwwroot/map/`, `route-colors.js`, `MapContract.cs`, `MapProxyAllowlistTests.cs` | Separate commit. Verified nothing loaded it first. The allow-list existed to stop the proxy becoming an open gateway under the service account's credentials — with no proxy there is no gateway. One endpoint kept, `/api/map/transport-types`, which the admin console uses as a reachability probe. The `MapAssets` allow-any-origin CORS policy and the `/map` CSP branch went too |
| **One-way rule amended** | `AGENTS.md`, `docs/map-proxy-migration.md`, `docs/reference/*` | The client uses GetThereAPI for all business data and reads TransitInfoAPI for the map alone. The H5 migration doc is marked superseded rather than deleted — it explains why the proxy was built, which is what makes removing it legible |

**Verification.** The page's chrome was exercised in a headless browser against stubbed endpoints:
chips render and toggle in both languages, the all-off case restores everything, the mode filter
reaches the map layers in all four states, the layers button toggles route lines, and search renders
results, flies to a pick and opens the sidebar. **The C# was not compiled** — this container has no
.NET SDK and the installer is blocked by egress policy, same as the 07-30 audit. Everything under
*Verification* in the plan that needs a running API, a device or an emulator is still outstanding;
the Android https/dev-cert path is the most likely thing to bite and cannot reproduce on Windows.

### MapLibre vendored into `wwwroot`

| Area | File(s) | What |
|------|---------|------|
| **Library vendored** | `TransitInfoAPI/wwwroot/vendor/maplibre-gl/` | MapLibre GL JS 4.7.1 (`maplibre-gl.js`, `maplibre-gl.css`) from the npm tarball, with `LICENSE.txt` beside it because the BSD notice has to travel with the code, and a README giving the update command and the version to bump |
| **Four pages repointed** | `map/public.html`, `map/index.html`, `admin/reconciliation-map.html`, `admin/shape-editor.html` | All were loading it from `unpkg.com`. That made a public CDN a hard runtime dependency, and it had grown teeth: the map's chrome now lives in `map/public.js`, a script that never runs if `maplibregl` is undefined, so a CDN failure took the search box and mode chips down with the basemap |
| **CSP tightened** | `TransitInfoAPI/Program.cs` | The map policy drops `unpkg.com` from `script-src` and `style-src` — **no external origin may execute script on that page at all** now. The admin policy drops it too; `cdn.jsdelivr.net` stays for Bootstrap |

**Verified** in a headless browser against the tightened policy, served with the same CSP
`Program.cs` sends: `maplibregl.getVersion()` returns `4.7.1`, the map canvas constructs, the chrome
renders, and there are **zero CSP violations**. The only off-origin request the page makes is to
`tiles.openfreemap.org`.

**This does not make the map work offline**, and the run above happens to prove it: that host is
blocked in the build sandbox, so the map rendered with controls, scale bar and attribution but an
empty basemap. Vendoring removes the CDN serving the *code*; tiles, glyphs and sprites are still
fetched at runtime. Offline means self-hosting or packaging tiles — a much larger piece of work, and
`docs/architecture/map-features.md` already lists "offline map & routing" as a feature in its own
right.

**Noticed while here, not fixed:** `admin/shape-editor.html` loads `mapbox-gl-draw` from
`https://api.mapbox.com`, an origin the admin CSP has never allowed. That plugin is therefore already
blocked and its drawing toolbar cannot be working. Vendoring it or allowing the origin is a separate
decision; a comment in `Program.cs` records it next to the policy.

### Refresh tokens are no longer pinned to an IP address

Authorised departure from the `AGENTS.md` off-limits list, made deliberately rather than as a side
effect of the offline work it was blocking.

| Area | File(s) | What |
|------|---------|------|
| **Address no longer decides the verdict** | `SharedAuth/RefreshTokenEvaluator.cs` | `Evaluate` drops its two address parameters. An `Invalid` verdict is a 401, and the MAUI client answers a failed refresh by clearing its credentials — so the check fired on every wifi-to-cellular handover, cell handover, CGNAT rebinding and IPv6 privacy-extension rotation. For a travel app whose users are by definition moving, that is repeated sign-outs, and it would have taken every offline-cached ticket with it |
| **Signal kept** | same, plus both `AuthManager.RefreshAsync` | New pure `IsAddressChange`, used only to write a `RefreshAddressChanged` audit row. Verdict logic and forensics are separate functions so the distinction is visible in the type. `RefreshToken.IpAddress` is still stored |
| **Tests** | `tests/GetThere.Tests/Auth/RefreshTokenEvaluatorTests.cs` | The two tests that encoded the old behaviour were rewritten to assert the new behaviour rather than deleted — a silently removed test is how a control comes back by accident. `Theft_detection_is_unaffected_by_the_address` is the regression guard; `IsAddressChange` gains its own coverage including both-null |
| **Docs** | `AGENTS.md`, `getthere-api/architecture.md`, `getthere-api/endpoints.md`, `transitinfo-api/endpoints.md`, `db/getthere-schema.md`, `overview.md` | The "IP binding, with a deliberate hole" section is replaced by "The address is recorded, not enforced", carrying the reasoning below |

**Why removing it does not weaken the system.** Rotation plus reuse detection is the actual theft
response and is untouched: a stolen token replayed after the legitimate client refreshes hits
`hasReplacement`, and the user's whole token family is revoked and audited. That holds regardless of
address. The address check only added value in the window before the next legitimate refresh, and
only against an attacker on a different address — while both APIs call `UseForwardedHeaders()` with
`KnownIPNetworks` and `KnownProxies` cleared, so `X-Forwarded-For` is honoured from any immediate
peer and an attacker holding a stolen token could simply assert the address the check wanted. It
punished honest mobile users reliably and a capable attacker not at all, and it could not distinguish
"user moved" from "thief", so no stricter or looser version would have been better.

**What would earn its place instead:** a binding to the *device* — a client-generated identifier that
survives a change of network but not a change of hardware. `RefreshToken.DeviceInfo` is not that; it
is the raw `User-Agent`, caller-supplied and not unique. Recorded as a follow-up, not built here.

**Not compiled.** No .NET SDK in this container; CI's `build-check.yml` is the gate.

### Ticket payloads are drawn as scannable codes

The wallet had never rendered one. `TicketDetailPage` showed the payload as monospace text inside a
dashed square standing in for a QR, and its own header comment said why: *"Turning the payload into a
true QR bitmap needs a QR encoder package, which the solution does not currently reference."* Nothing
server-side generated an image either — `ZXing.Net` was there only to *decode* uploads. A wallet whose
ticket cannot be scanned at a barrier is not a wallet.

| Area | File(s) | What |
|------|---------|------|
| **The decision** | `GetThereShared/Common/TicketBarcode.cs`, `Enums/BarcodeSymbology.cs` | Which symbology a payload may be drawn as, or none. Put in GetThereShared, away from any encoder, because the test project cannot reference the MAUI project and this is the part worth covering |
| **The rendering** | `GetThere/Services/BarcodeRenderService.cs`, `GetThere.csproj`, `MauiProgram.cs` | ZXing encodes, SkiaSharp rasterises to PNG. `ZXing.Net` was already in `Directory.Packages.props` for the API's decoder, so the client reference pins nothing new |
| **The screen** | `GetThere/Pages/TicketDetailPage.xaml`, `ViewModels/TicketDetailViewModel.cs` | The code where the placeholder was, with the payload text retained as the fallback branch. `TicketResponse.Format` is read for the first time — it had never been consumed by the client |
| **Tests** | `tests/GetThere.Tests/Tickets/TicketBarcodeTests.cs` | Nine cases over the choice, including the lossy-format trap below |

**The format discriminator is lossy, and refusing to guess is the design.** `TicketFormat` has five
values, but `BarcodeDecoder.ToTicketFormat` collapses everything that is not QR or DataMatrix into
`Barcode` — including Aztec and PDF417, which is exactly what UIC 918-3 rail tickets use and which
that decoder explicitly reads. So `Barcode` may mean a short linear code or a compressed binary rail
payload, and re-encoding the latter as Code 128 would produce a symbol that scans to the wrong bytes.
`ChooseSymbology` returns null whenever the payload will not round-trip, and the screen falls back to
text. An honest non-answer beats a confident wrong code at a gate.

The real fix is for the stored format to carry the true symbology rather than a five-value
approximation; that is a contract and storage change, recorded as a follow-up.

Rendered at 720px and scaled **down** — a scanner reads modules, and upscaling a small bitmap blurs
their edges until it stops reading. PNG, not JPEG: lossy artefacts land exactly on the module
boundaries a scanner measures. QR uses error-correction level Q, since this is read off a phone
screen where glare and fingerprints eat modules.

**Not compiled, and not yet scanned.** No .NET SDK in this container. The unverified risk is precisely
the one tests cannot cover: a code that renders but does not scan. It must be read by a real scanner,
per format, before this is trusted.

### Every ticket is reachable from the wallet

The Tickets tab listed **only imported tickets**. `TicketsViewModel` had one collection,
`ObservableCollection<ImportedTicketResponse>`, and `GET /tickets` was never surfaced as a list — so a
purchased ticket was visible for the few seconds after buying it, via the direct navigation in
`TicketPurchaseViewModel`, and then unreachable. That contradicts the product's own premise: *"one app
that holds **every** ticket a traveller has — the ones it sold them and the ones they already had"*.

| Area | File(s) | What |
|------|---------|------|
| **One list, both kinds** | `ViewModels/WalletTicket.cs`, `TicketsViewModel.cs` | A projection of either contract onto what a card shows. The two contracts share no base type and should not — separate tables, lifecycles and status enums — so this is a view concern, not a model one. Property names deliberately match the bindings the template already used, so the card did not have to be rewritten |
| **Tap opens the ticket** | `Pages/TicketsPage.xaml` | A tap used to raise an action sheet, and before that invoked Cancel outright — the list's primary gesture was destructive and the ticket itself could not be opened. Cancel/mark-used moved to a `⋯` control, shown only where the actions can actually work |
| **Imported tickets have a detail screen** | `Pages/ImportedTicketDetailPage.xaml(.cs)`, `ViewModels/ImportedTicketDetailViewModel.cs` | They had none at all, so `RawPayload` — decoded on import, and the thing a barrier scans — was written once and never shown. Now it renders through `BarcodeRenderService` |

Three states for the code panel, said differently on purpose: a drawn barcode; "no scannable code on
this ticket" for one typed by hand or imported from a file with no code in it; and the raw payload as
text when there is one but the renderer declined to redraw it. Collapsing the last two would tell a
user their ticket is empty when it is not.

Purchased tickets get no actions menu. Nothing in the API moves a purchased ticket out of `Active` —
the expiry worker is the only writer — so offering one would be a button that cannot work.
`GET /tickets` is unpaged, so it is re-read whenever the imported half loads its first page and
filtered client-side to match the status chips.

**Not compiled.** No .NET SDK in this container; CI is the gate.

### Tickets survive having no signal

The client persisted nothing at all — no SQLite, `AppDataDirectory` never touched, every screen a
live HTTP read on appear. Offline meant an empty list and an error label, which for a travel wallet
is backwards: a ticket is most needed at a barrier, which is where signal is worst.

| Area | File(s) | What |
|------|---------|------|
| **The store** | `GetThere/Services/TicketStore.cs` | JSON per collection under `FileSystem.AppDataDirectory`, written temp-then-move so a process killed mid-write cannot leave a truncated file. A write lock because two screens can finish loading at once |
| **Whose data it is** | `Services/AuthService.cs` | `GetOwnerKeyAsync` — the `sub` claim when signed in, read **without checking expiry** because it must answer while offline with a lapsed token; a persisted generated id otherwise. Directories are named by a hash of that key, so a user id never appears in a path |
| **Read and write rules** | `ViewModels/TicketsViewModel.cs` | Written as a by-product of a successful unfiltered first-page read; read **only** from a failure path, so a bug here cannot serve a stale ticket to someone who is online |
| **Provenance** | `Pages/TicketsPage.xaml` | "Saved 3 h ago · showing your last update" whenever the list came off the device. Coarse on purpose — that is what a traveller needs to judge the screen, and a precise timestamp would imply a precision the cached *status* does not have |

Keyed by owner because a device is not a person: two accounts, or an account and the guest who used
the phone before them, must never see each other's tickets. Only the unfiltered page is cached — a
stored copy of "the Used ones" would be a strange thing to show someone offline.

`Clear` exists for an explicit sign-out and is deliberately **not** called from the 401 path in
`AuthenticatedHttpHandler`: that fires when a refresh is rejected, which is not always the user's
decision, and wiping their tickets in response would remove the cache in exactly the situation it
exists for.

Still to come in this series: cached tickets are written in clear text, and `allowBackup="true"` means
they would reach cloud backups. Encryption keyed from `SecureStorage` is the next slice.

**Not compiled.** No .NET SDK in this container; CI is the gate.

### Cached tickets no longer claim to be active after their window closes

Necessary companion to the cache above. Status is a stored column the server owns —
`TicketExpiryWorker` sweeps hourly and nothing else writes it — so a ticket whose window shut ten
minutes ago still reads `Active`, and one served from the device's cache can be far staler. Showing
`Active` over a closed window is the one failure that matters at a barrier.

`GetThereShared/Common/TicketValidity.cs` decides it, display-only, and is applied in `WalletTicket`,
`TicketDetailViewModel` and `ImportedTicketDetailViewModel`. The result is never written into a
status field, sent to the server, or used to gate an API call — `AGENTS.md` puts ticket status
transitions off-limits, and this stays firmly on the display side of that line.

Four rules, each with a reason:

- **Downgrade only.** A ticket already `Used`, `Cancelled` or `Expired` is left alone. Those come
  from an explicit action, possibly on another device, and are unknowable offline — recomputing them
  would resurrect a cancelled ticket to active, which at a barrier looks like fare evasion.
- **A null `ValidTo` never expires**, matching the sweep, which requires it to be non-null, and SQL,
  where a comparison against NULL is never true.
- **`ValidTo < now`, strictly**, matching the worker. A boundary that disagreed would make the same
  ticket flip state on reconnect.
- **UTC always.** A timestamp off the wire can arrive as `DateTimeKind.Unspecified`; comparing that
  to local time is wrong by the device's offset. That confusion already caused a bug on the write
  path — see `ImportedTicketManager.ToUtc`.

Seven tests, written around proving the rule cannot upgrade a status rather than that it can
downgrade one.

### Cached tickets are encrypted and kept out of backups

A barcode payload is a bearer credential for travel: whoever renders it rides. Until now the cache
above wrote them in clear text into `AppDataDirectory`, which is app-private — enough against another
app, not against a rooted device or an ADB pull — and `allowBackup="true"` meant they would also
travel to Google's servers.

| Area | File(s) | What |
|------|---------|------|
| **At rest** | `Services/TicketStore.cs` | AES-GCM, key generated on first use and held in `SecureStorage` — the same store already holding the auth tokens, which puts the payloads at parity with the credentials they are equivalent to. Layout is nonce ‖ tag ‖ ciphertext, nonce fresh per write |
| **Off the wire** | `AndroidManifest.xml`, `Resources/xml/backup_rules.xml`, `Resources/xml/data_extraction_rules.xml` | The tickets directory is excluded from Auto Backup **and** from device-to-device transfer. Two files because Android picks by API level — 23-30 reads `fullBackupContent`, 31+ reads `dataExtractionRules` — and an exclusion added to only one silently stops applying on half the fleet |
| **On sign-out** | `ViewModels/ProfileViewModel.cs` | The explicit sign-out clears the store, and only it. Order matters: the owner key comes from the access token, so it is resolved *before* `Logout` clears it |

Authenticated encryption rather than plain AES, so a file edited on a rooted device fails its tag
check instead of deserialising into a ticket whose contents someone else chose. A failed decrypt is
treated exactly like a missing file — the screen shows its ordinary offline state rather than a
second, stranger error.

The 401 path in `AuthenticatedHttpHandler` also calls `Logout`, and deliberately does **not** clear:
that fires when a refresh is rejected, which is not a decision the user made, and deleting their
offline wallet in response would remove the cache in exactly the situation it exists for.

Backup stays enabled overall — losing a wallet's settings on a handset swap would be a poor trade.
Only the ticket directory is excluded. Its key lives in `SecureStorage`, which Android does not back
up, so a restored copy would be undecryptable anyway; the exclusion keeps the credential off the wire
rather than depending on that.

### Importing a ticket no longer needs an account or a connection

The last of the offline series, and the one that changes what the app is for. Every import path
called an authorized endpoint before the confirmation form opened, and `Save` unconditionally posted
to the server — so a signed-out user could not import a ticket, and neither could a signed-in one
with no signal. For a wallet whose premise is holding tickets the user already has, both were the
wrong way round.

| Area | File(s) | What |
|------|---------|------|
| **Extraction became shared** | new `GetThereExtraction/` | `BarcodeDecoder`, `ImageTicketExtractor`, `TicketTextScraper`, `TicketFileSniffer` and `ITicketExtractor` moved out of GetThereAPI. Same code both sides, so a ticket read on a device and one read on the server cannot disagree — which serves the original "behaves identically on every platform" intent better than server-only did |
| **The device can read a ticket** | `GetThere/Services/LocalExtractionService.cs` | Photo and pasted text, with no network. `CanExtractLocally` is how a caller finds out before promising the user anything |
| **Local-first saving** | `ViewModels/ImportTicketViewModel.cs`, `Services/PendingImportQueue.cs` | Saved to the device, pushed later. A failed create now queues rather than losing what the user typed |
| **Idempotency** | `Entities/ImportedTicket.cs`, `ImportedTicketContract.cs`, `AppDbContext.cs`, migration `20260731120000` | `ClientId`, minted at creation, unique per user. `CreateAsync` checks it **first** and returns the original on a replay |
| **Guest → account** | `Services/ImportSyncService.cs`, `ViewModels/LoginViewModel.cs`, `TicketsViewModel.cs` | The queue drains on sign-in and on every wallet load. Nothing did this before — `ClearGuest` ran on logout and never on login, so a guest who imported and then signed up would have found an empty wallet |

**Why the dedupe hash could not be the idempotency key**, which is the whole reason for a new column:
it is computed from the request's fields, so a ticket edited between being queued and being pushed
hashes differently and inserts twice; and its unique index is filtered on `Status = 'Active'`, so one
marked used before the queue drained inserts again. The new index is filtered on `ClientId IS NOT
NULL` only — deliberately *not* on status — so a retry finds the original whatever became of it.
`ImportedTicketClientIdTests` pins both failure modes against the real hash function, so that if
either ever stops being true someone can see the column is no longer earning its place.

**What stayed server-side:** PDF (PdfPig), calendar invites (Ical.Net) and wallet passes. The first
two are the heaviest dependencies and the AOT/trimming risk on iOS is real; `PkPassTicketExtractor`
is otherwise portable but throws GetThereAPI's `AppException`, and untangling that is its own change.
Those formats still need an account, and the UI says so rather than failing obscurely.

**A guest's wallet now shows their tickets** instead of a full-screen "account required" scrim. They
are marked as device-only and cannot be opened — they have no server id, and both detail screens
fetch by one.

> **The migration is hand-written.** `20260731120000_AddImportedTicketClientId` and the matching
> `AppDbContextModelSnapshot` edit were authored without `dotnet ef`, because the environment had no
> .NET SDK — a deliberate exception to `AGENTS.md`'s "never manually edit `*ModelSnapshot.cs`",
> granted for this change. Re-scaffold it against a real toolchain before it reaches a shared
> database. The DDL is two statements; the snapshot agreeing with the model is the part worth
> checking.
>
> **It was never valid, and this was caught on 2026-08-02 — see that session below.** The file had no
> `[Migration]` attribute and no `.Designer.cs`, so EF never discovered it: it never appeared in
> `migrations list` and `database update` skipped it while reporting success. The DDL was correct
> and the snapshot did match the model; the missing piece was the metadata that makes a migration
> *run*.

---

## Session — August 2, 2026

### GetThereAPI's TransitInfoAPI dependency removed entirely

The map migration (July 31) moved the map page to TransitInfoAPI and left the proxy with one
endpoint: `GET /api/map/transport-types`, which no client called. The **admin console** used it as a
reachability probe, lighting a status dot in the rail on any success — and that call was the only
remaining reason GetThereAPI held TransitInfoAPI service-account credentials.

The probe authenticated with the service account in order to prove the service account worked. Once
nothing else used that credential, the check was circular: it verified the health of a thing whose
sole consumer was the check itself. Deleting it is the end state the map migration implied.

| Area | File(s) | What |
|------|---------|------|
| **Upstream client deleted** | `GetThereAPI/Services/TransitInfoApiClient.cs` | 487 lines: service-account login, `static` token cache with double-checked locking, 5-minute expiry margin, single 401 retry, `FetchAllPagesAsync`, the 502 wrapper and the hand-maintained mirror DTOs |
| **Proxy remnants deleted** | `MapManager.cs`, `MapProxyController.cs` | The last manager/controller pair in the map path. `MapManager` needed no DI removal — `GetThereAPI.Managers.*` is auto-registered by namespace scan, so deleting the file was sufficient |
| **DI + config** | `Program.cs`, `appsettings.json` | `AddHttpClient<TransitInfoApiClient>` and `Configure<TransitInfoApiOptions>` dropped; the whole `TransitInfoApi` block went with them, including the `"ClientSecret": "CHANGE-ME"` placeholder |
| **Permission retired** | `Common/PermissionKeys.cs` | `MapView` (`map.view`) removed from the constant, `All`, `UserRoleDefaults` and `Meta`. Seeding is claim-**additive**, so existing role claims keep `map.view` in the database — a stale claim no code reads. Delete it by hand if that matters |
| **Admin console** | `wwwroot/admin/admin.js` | Rail status dot, its markup and `probeTransitInfo` removed. The rail foot keeps the "one-way · GlobalId reference" line, which is still accurate |
| **Docs** | `AGENTS.md`, `VERIFY.md`, `docs/README.md`, `docs/map-proxy-migration.md`, `docs/architecture/integration-guide.md`, `docs/reference/{overview,shared/contracts}.md`, `docs/reference/getthere-api/{architecture,endpoints,transit-integration}.md` | Boundary 2 marked removed; `transit-integration.md` marked superseded but **kept in full** — the allowlist argument and the 502-not-500 rule are what any future integration has to answer |

**What deliberately stays.** `TicketingAdapter.TransitInfoGlobalId` is untouched: an indexed column
with live data, rendered in the adapters admin page and mapped in `AdminManager`. It is a **string
soft reference** to an operator's Onestop ID, not a foreign key and not a call — removing it is a
migration and a data loss, which is a different change from unwiring an integration.

**Consequences worth knowing.** Cold-start order no longer matters — TransitInfoAPI had to come up
first because it creates the `getthere-api` service account. The `Seed:ServiceAccountPassword` /
`TransitInfoApi:ClientSecret` split, previously the most common integration failure between the two
services, no longer exists. The `getthere-api` account remains upstream, dormant. The shared
`IMemoryCache` `SizeLimit = 2_000` was introduced for `MapManager`'s viewport-keyed reads and is kept
despite losing that consumer; `DynamicClaimsTransformation` is now its only user.

**Verification.** `dotnet build` clean (0 warnings, 0 errors) and the full suite green at **303/303**.
Both had to be built to a scratch `BaseOutputPath` — GetThereAPI and TransitInfoAPI were running
locally and held file locks on `bin/`, which surfaces as MSB3021/MSB3027 copy errors rather than
compile errors.

### Fixed: `Invalid column name 'ClientId'` on the tickets and journeys screens

Reported from the MAUI app as an unexpected error on both screens. Not an app-level error code —
there is no `INVALID_CLIENT_ID` anywhere in the repo — but SQL Server's own message surfacing
through the generic error path.

**`ImportedTickets.ClientId` did not exist in `GetThereDB`, while the EF model expected it.** Every
generated SELECT included the column, so every query against `ImportedTickets` failed: the tickets
screen directly, and the journeys list through `.Include(j => j.ImportedTickets)`
(`JourneyManager.cs:70`).

The cause was the hand-written migration from July 31. `20260731120000_AddImportedTicketClientId`
carried **no `[Migration]` attribute and no `.Designer.cs`**. That attribute is how EF discovers
migrations, so the class — despite inheriting `Migration` and containing correct DDL — was invisible
to the tooling. `dotnet ef migrations list` did not show it and `dotnet ef database update` skipped
it while reporting `Done.`, which is why the drift went unnoticed for two days.

| Area | File(s) | What |
|------|---------|------|
| **Migration re-scaffolded** | `Migrations/20260802130743_AddImportedTicketClientId.{cs,Designer.cs}` | `AppDbContextModelSnapshot.cs` restored from `4318c52^` so the model diff was non-empty, the invalid file deleted, then `dotnet ef migrations add`. The generated DDL is **identical** to the hand-written version — the SQL was always right |
| **Reasoning preserved** | same | The filtered-index rationale (`ClientId IS NOT NULL`, because SQL Server treats NULLs as equal in a unique index; deliberately *not* filtered on `Status`, because a retry must find the original whatever became of it) was carried over from the deleted file, plus a note on why the original silently did nothing |
| **Applied** | `GetThereDB` | Verified in `sys.columns` / `sys.indexes`: `ClientId uniqueidentifier NULL`, unique index filtered `([ClientId] IS NOT NULL)` |
| **Docs** | `VERIFY.md` §2, this file | The July 31 caveat marked resolved rather than deleted |

**Two migrations were also pending and unrelated** — `20260728153241_AddPurchaseStatusIndex` and
`20260728155730_AddExpirySweepIndexes` — and applied in the same run. GetThereAPI does not migrate on
startup (only TransitInfoAPI does, and only in Development), so migrations here are a manual step and
drift like this is invisible until a query hits the gap.

> **Why the test suite could not catch this.** Tests build their schema from the EF model, so a
> migration that never executes is indistinguishable from one that does — 303/303 passed throughout,
> both before and after the fix. Only a query against a real database exposes it. Worth remembering
> before trusting a green suite as evidence that a schema change landed.

### TransitInfoAPI admin: navigation on every page, not just the overview

The console had a rail on exactly one of its fifteen screens. Every other page was a Bootstrap
container whose only navigation was a `← Home > Admin` text breadcrumb, so moving between screens
meant going back to the overview or typing a URL. The July 31 restyle gave those pages the dark
palette via `body.bs` but never mounted the shell on them, which is why they looked roughly right
and behaved badly.

`admin-shell.js` already listed all fourteen destinations in `NAV` — nothing needed designing, only
mounting.

| Area | File(s) | What |
|------|---------|------|
| **Shell refactor** | `admin-shell.js` | Rail/topbar markup extracted from `mount` into a private `buildShell(page, contentHtml)`. `mount` is unchanged in behaviour; the chrome now has one definition instead of two |
| **`Shell.mountLegacy`** | same | Mounts the chrome around a page that still renders Bootstrap, **relocating** its existing markup into the content area rather than replacing it. Nodes are moved, not re-serialised through `innerHTML`, so listeners bound by an earlier script survive. The content div deliberately carries **no `id`** — every legacy page already owns an `id="content"` its own script writes into, and a second one would silently break whichever the script reached first |
| **12 pages mounted** | `agencies`, `alerts`, `countries`, `feed-versions`, `feeds`, `mobility`, `operators`, `places`, `realtime`, `reconciliation`, `routes`, `stations` `.html` | `<div id="shell">` + `<div id="page" hidden>` wrapper, the text breadcrumb dropped (the rail replaces it), the in-page `<h1>` dropped (the topbar owns the title), and the header row switched to `justify-content-end` so the action buttons stay right-aligned without it. `realtime.html` keeps `space-between` — its live indicator sits where the h1 was |
| **Layout** | `style.css` | `body.bs.has-shell` drops the `padding-top` that suits a bare centred container and fights a full-height rail; `.content.is-legacy` resets the Bootstrap container's centring and max-width, since `.content` already provides the gutter |

**No page script was touched.** All 4,583 lines of `*.page.js` are untouched, and the inner tables,
filters and modals keep their Bootstrap markup — already dark via the `body.bs` skin. Porting that
content onto the design-system primitives is a separate, much larger pass.

**Two pages were deliberately left alone.** `reconciliation-map.html` and `shape-editor.html` are
full-bleed map surfaces with fixed-position overlays, reached from a parent screen and carrying their
own back/cancel affordance. A rail would shrink the map and fight those overlays, and neither is a
navigation destination.

#### Regression, same session: every mounted page rendered black

The first version wrapped each page in `<div id="page" hidden>` and relied on `mountLegacy` to
reveal it by relocating the content. **That makes every failure catastrophic.** Anything that stops
the mount call from completing — a browser holding a cached copy of the previous `admin-shell.js`,
which has no `mountLegacy` and throws `TypeError`; a script that fails to load; an auth redirect
mid-flight — leaves the attribute in place and the screen entirely blank. Static files here are
served with only `ETag`/`Last-Modified` and no `Cache-Control`, so a stale cached script is an
ordinary occurrence, not an edge case.

**Fixed by never hiding the content.** `hidden` is gone from all twelve wrappers; the markup renders
in place and `mountLegacy` moves it. The worst case is now the page looking as it did before the
shell existed — no rail, but readable and fully usable. The reasoning is recorded in the
`mountLegacy` doc comment so it does not get reintroduced.

The lesson generalises past this change: **an enhancement that hides content until JavaScript
succeeds converts every failure into a blank page.** Relocate visible content instead of revealing
hidden content, and the same code degrades instead of disappearing.

**Verification.** Tag balance checked on all twelve (every `<div>` closed), plus `#shell` host,
single `mountLegacy` call, `admin-shell.js` loaded before the page script, and the served CSP
(`script-src 'self' 'unsafe-inline' …`) confirmed to permit the inline mount call.

Both paths were then exercised in a real browser against the live CSS, for all twelve pages, by
parsing each page and replaying the mount: **0 failures**. Each was checked to render its content
with the shell absent (the degraded path), and on mount to produce 14 rail items, the right topbar
title, the container relocated into the content area, and no `#page` wrapper left behind.

### Admin console: icons were blocked by CSP, and stale assets are no longer possible

**Icons.** Every `<i class="bi bi-…">` in the TransitInfoAPI console rendered as nothing — 59 of
them across the legacy pages. The cause was the admin CSP, which listed `script-src` and `style-src`
but **no `font-src`**. An omitted directive does not mean "unrestricted": it falls back to
`default-src 'self'`, so the Bootstrap Icons *stylesheet* loaded from jsdelivr while the *webfont* it
points at was blocked. That failure is silent apart from a console violation, which is why it read
as "icons are buggy" rather than as a policy problem.

Fixed by listing `font-src 'self' data: https://cdn.jsdelivr.net` explicitly. This does not widen
script execution — jsdelivr may already serve script and style to this console.

The design-system icons were never affected: they are same-origin SVG mask images under
`/admin/icons/`, allowed by `default-src 'self'` and by `img-src`.

**Caching.** Neither console sent `Cache-Control`, leaving `ETag`/`Last-Modified` as the only
freshness signals — which a browser may decline to revalidate. A shipped change to `admin-shell.js`
or `style.css` therefore reached an open tab only on a hard refresh. This is what turned the
`mountLegacy` rollout into a black screen: tabs holding the previous `admin-shell.js` called a
function that did not exist in it.

| Area | File(s) | What |
|------|---------|------|
| **font-src** | `TransitInfoAPI/Program.cs` | Added to the `/admin` CSP, with the reasoning recorded — the trap is that omitting a directive silently inherits `default-src` |
| **Cache-Control** | `TransitInfoAPI/Program.cs`, `GetThereAPI/Program.cs` | `no-cache` on every `/admin` asset in **both** consoles. Not `no-store`: caching is still allowed, it just requires a conditional request, which the existing `ETag` answers with a cheap 304 |
| **Launch config** | `.claude/launch.json` | Added so the APIs can be started for browser verification. TransitInfoAPI uses `--launch-profile https`, as its map page requires |

**Verification, in a real browser against the running service.** `Cache-Control: no-cache` confirmed
on both the HTML and `admin-shell.js`. `font-src` confirmed present in the served policy. The
Bootstrap Icons webfont went from `BLOCKED` to `LOADED`, and a `bi bi-arrow-clockwise` glyph
measured 25px wide — it renders. Suite green at 303/303.

> **Both facts had to be checked against a freshly-loaded document.** The first probe reported the
> font still blocked, because the page under test had itself been served from cache carrying the
> *old* CSP — the very problem being fixed, appearing as a false negative in its own verification.
> Worth remembering when testing a header change: the document doing the testing may predate it.

**GetThereAPI's console needed only the cache header** — it has no Bootstrap Icons at all (the CDN
was dropped there in the July 31 restyle), so its CSP has no font to allow.

---

## Session — August 8, 2026

### GetThereExtraction folded into GetThereShared

Six projects became five. `GetThereExtraction`'s consumer set was *identical* to
`GetThereShared`'s — the MAUI app, GetThereAPI and the test project referenced both, nothing
referenced one without the other — and it already depended on `GetThereShared`. A separate
assembly for that bought nothing except another node in the reference graph and four more steps in
`build-check.yml`.

The five files moved to `GetThereShared/Extraction/`, namespace `GetThereExtraction` →
`GetThereShared.Extraction`. `ZXing.Net` and `SkiaSharp` moved with them; every consumer already
referenced both directly, so nothing new is pinned.

**Nothing about the extraction split changed.** `PdfTicketExtractor`, `ICalTicketExtractor` and
`PkPassTicketExtractor` stay in GetThereAPI for the reasons recorded when they were left behind
(PdfPig and Ical.Net weight, AOT/trimming risk on iOS, and PkPass throwing GetThereAPI's
`AppException`). Device-side import still covers images and pasted text only.

| Area | File(s) | What |
|------|---------|------|
| **Move** | `GetThereShared/Extraction/*.cs` | 5 files, namespace rewritten; git tracked all five as renames |
| **Packages** | `GetThereShared.csproj` | `ZXing.Net` + `SkiaSharp` added, with the rationale from the deleted project carried over |
| **References** | `GetThere.csproj`, `GetThereAPI.csproj`, `GetThere.Tests.csproj`, `GetThere.slnx` | `GetThereExtraction` ProjectReference and solution entry dropped |
| **Usings** | 8 consumer files | `using GetThereExtraction;` → `using GetThereShared.Extraction;`, merged into the existing GetThereShared group |
| **CI** | `.github/workflows/build-check.yml` | Restore/build/lint steps and the vulnerability-scan project list |
| **Docs** | `docs/reference/overview.md`, `getthere-client/architecture.md` | Project table and the guest-import paragraph |

**One caveat worth recording.** `GetThereShared` was previously package-free — pure DTOs and enums.
It now carries a native graphics dependency. Nothing references it for contracts alone today, so the
cost is theoretical, but it is the reason the split was defensible and the reason to reconsider if a
contracts-only consumer ever appears.

**Verified.** GetThereShared, GetThereAPI and the test project build with `-warnaserror`; the MAUI
app builds for both `net10.0-windows` and `net10.0-android`; 39 extraction and sniffer tests pass;
`dotnet format --verify-no-changes` clean on all three non-MAUI projects.

---

## Session — August 10, 2026

### Full-solution audit, remediated in six waves

A read-only audit across all six projects (security, performance, magic numbers, stubs, tech debt,
bugs, hardcoding, architecture), then the fixes. Findings were concentrated in `TransitInfoAPI`,
which has had less hardening attention than `GetThereAPI`.

**No .NET SDK was available in the container this was written in.** Nothing here was compiled and no
test was run. The JavaScript was checked with `node --check`; the C# was not checked by anything.
Treat CI as the first real verification.

#### Wave 1 — credential exposure and data in git

| Area | File(s) | What |
|------|---------|------|
| **Secret at rest** | `Services/SecretProtector.cs` (new), `Managers/CustomSourceManager.cs`, `Services/CustomHttpSource.cs`, `Program.cs` | `CustomSource.AuthConfig` (operator bearer tokens, basic-auth passwords, API-key headers) was plaintext in the column. Now protected via `IDataProtection` at the two write sites and unprotected at the two that spend it. Applied in the manager, not as an EF value converter, so no migration and no DbContext change. Unprefixed legacy values pass through and are protected on next save. |
| **Secret over the wire** | `Contracts/CustomSourceContract.cs`, `Mapping/CustomSourceMapper.cs`, `wwwroot/admin/custom-source-editor.{html,page.js}` | `CustomSourceResponse.AuthConfig` → `HasAuth` bool. Every holder of `customsources.view` could read every operator credential from `GET /custom-sources`. The editor field is now write-only: blank keeps the stored value, which `UpdateAsync` already honoured. |
| **Dead credentialed account** | `Program.cs` | The `getthere-api` service account was still seeded on every boot, eight days after the map proxy that used it was removed. Nothing called it; it held `Client`; in Development it wrote its password to disk. `Seed:ServiceAccountPassword` is no longer read. |
| **Over-broad role** | `Program.cs` | `Client` was granted every `*.view` by suffix match, including `users.view` and `roles.view`. Narrowed, and stale claims are removed from existing databases rather than only skipped on new ones. |
| **User data in git** | `.gitignore`, `git rm --cached` | A user's ticket image (under their user id) and three operator spreadsheets were tracked. `/TransitInfoAPI/feeds` was already ignored; these two roots were missed. **History still contains them** — rewriting it was deliberately left as a separate decision. |

#### Wave 2 — input validation

- `[Range(1, int.MaxValue)]` on `page` across TransitInfoAPI's fifteen paginated actions. `?page=0`
  produced `OFFSET -50 ROWS` → `SqlException` → 500, on anonymous endpoints included. GetThereAPI's
  five were already bounded.
- `RoleController.GetUsers` `pageSize` bounded — the only paginated endpoint in either service with
  no ceiling at all.
- `/stations/{id}/departures` `count` bounded at 100, and `ScheduleManager`'s over-fetch multiply
  moved to `long` with a clamp: past ~358M it overflowed `int`, `Math.Min` picked the negative
  product, and `Take()` threw.
- **Station search reported the wrong total.** `GetTotalCountAsync` had no `q`/`routeType`
  parameters, so a search matching 8 rows advertised every station in the country. The four copies
  of the station predicates are now one `BuildQuery`.
- **`MobilityController` accepted `countryName` and passed it nowhere** — found while applying the
  same refactor. The admin console's country filter had been building the query string and having it
  ignored.
- Both bounding boxes clamp latitude before `Math.Cos` (Infinity at the poles).

#### Wave 3 — auth race, SSRF, OOM, lock contention

- **Refresh-token rotation was a read-modify-write.** Two concurrent presentations of one token both
  passed `Evaluate` as `Rotate` and both minted a successor — the unique index is on `Token` and the
  hashes differ, so nothing collided. Rotation only detects reuse if rotating is atomic. Now a single
  conditional `ExecuteUpdate` whose `WHERE` is the lock; losing the race is treated as reuse.
  Identical change in both APIs. **This is an authorised change to the JWT pipeline** — see
  `AGENTS.md`.
- **SSRF guard could not hold.** `EnsurePublicDestination` resolves, then `HttpClient` resolves again
  at connect — classic DNS rebinding. Moved to a `ConnectCallback` on both feed clients, which also
  covers redirect hops (`CustomSourceEngine` re-checked nothing after a redirect). `100.64/10`,
  `192.0.0/24` and `198.18/15` added.
- Custom-source responses capped at 32 MB; they were `ReadAsStringAsync` with no bound, in a loop of
  up to 500 pages.
- A credential-carrying source that redirects off-host is refused: `HttpClient` strips
  `Authorization` across origins but not the arbitrary API-key header `ApplyAuth` sets.
- `WalletTransaction.Type`/`ReferenceId` given lengths + a filtered unique index. Both were
  `nvarchar(max)` and unindexable, so `RefundAsync`'s `UPDLOCK, HOLDLOCK` guard locked a full table
  scan and serialised every refund. **Migration not generated** — see `docs/database-drift.md`.

#### Wave 4 — performance

- `EnableForHttps = true` on both APIs. Compression was registered and inert in every environment
  with HTTPS redirection, which is all of them.
- Reconciliation detail: two per-candidate queries hoisted out of an unbounded loop (~400 round
  trips for a station with 200 raw stops → 3).
- Station search keeps `Contains` — a prefix match would break "Glavni" finding "Zagreb Glavni
  Kolodvor" — but gains a three-character floor. Full-text index is the durable fix.
- TransitInfoAPI's `IMemoryCache` bounded; abandoned-upload sweep batched.
- **Bug:** `HandleImportErrorAsync` wrote `(int)FeedImportStatus.Failed` through raw ADO.NET into a
  column EF stores as the enum *name*, so SQL Server stored `"3"` and every
  `Where(fv => fv.ImportStatus == Failed)` matched none of those rows. Failed imports were invisible
  to the admin console's status filter.

#### Wave 5 — admin console

- `esc` was copy-pasted into twelve page scripts and `safeUrl` into three. Both now live on `Shell`;
  the copies delegate. That is the control stopping operator-supplied text becoming script.
- **`admin-auth.js` and `admin-shell.js` each had a refresh implementation with its own in-flight
  guard**, both loaded on every page but `index.html`. Two guards serialise nothing. Harmless until
  wave 3 — now the loser of that race trips reuse detection and revokes the operator's session
  family. `admin-auth.js` exports the single implementation; `Shell.refresh` defers to it, keeping
  its body only as the fallback for `index.html`.
- `'unsafe-inline'` **stays**. The comment claimed 111 inline handlers remained; the real count is
  212 across 17 pages, and 156 of them are built inside render functions, needing event delegation
  rather than a mechanical replace. No test covers the console and none could be run here.

#### Wave 6 — consistency

| Area | File(s) | What |
|------|---------|------|
| **Middleware order** | `TransitInfoAPI/Program.cs` | HSTS/redirect/static now run *before* authentication, matching GetThereAPI. A bearer token presented over plain http was being parsed, validated and used for a claims lookup before the pipeline redirected it to TLS. |
| **Duplication** | `SharedAuth/SeedPasswordGenerator.cs` (new), both `Program.cs` | Identical generator in both files. Also fixed: it drew uniformly from a combined alphabet and so did not *guarantee* the digit/uppercase/symbol the password policy requires — a miss meant `CreateAsync` rejected it and the environment came up with no admin. |
| **Localization** | `ImportTicketViewModel.cs`, both `.resx` | Three hardcoded English validation strings on the ticket-import path. Parity now 284/284. |
| **CI** | `.github/workflows/build-check.yml` | `dotnet format` step for the MAUI project — the one project of six without one. **It is non-blocking**; see the round-2 entry below for what it found and what is still owed. |

#### Findings withdrawn

Two audit findings were wrong and are recorded here so they are not re-reported:

- **`ImportLogStore` does clear on the failure path.** `HandleImportErrorAsync` has called `Clear`
  since it was written, with a comment explaining why.
- **GetThereAPI's paginated endpoints already bounded `page`.** Only TransitInfoAPI's did not.

Separately, the eight `NotImplementedException`s in `GetThere/Helpers/PageUtility.cs` are
`IValueConverter.ConvertBack` on one-way converters — idiomatic MAUI, not stubs.

#### Still owed

- The `AddWalletTransactionRefundIndex` migration (`docs/database-drift.md`).
- A full-text index on `CanonicalStations.Name`.
- The 212 inline handlers, and then `'unsafe-inline'`.
- Unchanged and still owed from before: a real `ITicketingAdapter`, a payment provider behind
  `/wallet/topup`, an email sender, password reset and email confirmation, and a real
  `ITicketFileScanner` in place of the no-op.

---

## Session — August 12, 2026

### Audit round 2, and the first CI run this branch ever had

Round 1 (previous entry) shipped uncompiled — the container had no .NET SDK. This session got CI
running against the branch, fixed what it found, and audited the areas round 1 had only sampled.

#### CI could not run on a feature branch at all

`build-check.yml` triggered on push to `main`/`develop` and pull requests targeting them, and carried
no `workflow_dispatch`. A branch could therefore not be compiled until someone opened a pull request
for it, which inverts the point — building it is how you find out whether it is worth proposing.
`workflow_dispatch` added.

#### What the first run found

**All four `-warnaserror` builds passed**, and 305 of 359 tests. The 54 failures were one cause:

> `PendingModelChangesWarning: The model for context 'AppDbContext' has pending changes.`

Round 1 added a filtered unique index and two column lengths to `WalletTransaction` without
generating the migration, on the stated assumption that a model change ahead of its migration is
inert. **It is not** — EF Core raises that warning as an *error* from inside `Database.Migrate()`,
which all three database-backed fixtures call in their constructors. The round-1 commit message said
"nothing regresses in the meantime"; that was wrong.

Both model edits were reverted (the wallet one and the `TransitInfoAPI` column sizes written the same
day). Neither finding is lost: the intended shape of each is a comment at the site, and
`docs/database-drift.md` carries both, the sizes to use, and what to check before generating each
migration. **The rule: a model change and its migration land in the same commit.**

The `maui` job was also already failing, before this branch touched it. `GetThere.csproj`
multi-targets, so on Windows `dotnet restore` resolves `net10.0-ios` and `net10.0-maccatalyst` while
only `maui-android` was installed — `NETSDK1147`. Fixed by installing the full `maui` workload; CI
still only *builds* Android.

#### New findings, fixed

| Finding | File(s) | What |
|---|---|---|
| **A config typo stopped the whole service** | `Workers/PollingInterval.cs` (new), `RealtimePollingWorker.cs`, `MobilityPollingWorker.cs`, `FeedPollingWorker.cs` | Two of the three workers passed `IOptionsMonitor.CurrentValue` straight to `Task.Delay`. Zero spun the loop; **negative threw `ArgumentOutOfRangeException` from a `Task.Delay` outside the try/catch**, so it escaped `ExecuteAsync` where the default `BackgroundServiceExceptionBehavior.StopHost` stops the service. `InitialDelaySeconds` runs before the loop, so that one killed the host at startup, and `CurrentValue` is re-read each cycle so a bad hot-reload could stop a healthy service. GetThereAPI's workers already clamped, with the reason written down; that idea is now shared. |
| **Unmerge was not atomic** | `ReconciliationManager.cs` | `UnmergeStationsAsync` had no transaction while the merge it reverses does. `ExecuteUpdateAsync` moves the StopTimes back and commits immediately; `SaveChangesAsync` then writes the RawStop reassignment, candidate moves and source reactivation. A failure between left StopTimes pointing at the source while its RawStops still belonged to the target — departures resolving through a station whose stops are elsewhere. Same shape as the interrupted-import bug `Program.cs` documents. |

#### New findings, recorded not fixed

- **`nvarchar(450)` on every indexed string column in TransitInfoAPI.** EF widens an indexed string
  with no length to the 900-byte key limit — so the indexes exist (unlike the `nvarchar(max)` case)
  but each key reserves up to 900 bytes for a 2-character ISO code or a short GTFS id, on
  `StopTimes` and `Trips`, the two largest tables. GetThereAPI reached this conclusion already, on
  `Purchase.Status`. Sizes and verification steps in `docs/database-drift.md`; needs a migration.
- **Merge leaks operator links.** `MergeStationsAsync` copies `CanonicalStationOperator` rows onto
  the target and `UnmergeStationsAsync` never removes them, so a merge/unmerge cycle leaves the
  target permanently claiming the source's operators. Fixing it needs a decision — record the created
  links in the merge log (schema change) or re-derive support as `FeedManager` does — so it is
  documented at the site rather than patched blind.
- **`map/public.js`'s `esc()` does not escape quotes.** It round-trips through `textContent`, which
  escapes `<`, `>` and `&` but not `"` or `'`. Every current use is a text context, so it is correct
  today — but the admin console's `esc` *does* escape quotes, and anyone assuming these match while
  interpolating into an attribute would introduce an injection. The `/map` CSP has no
  `'unsafe-inline'`, which is the backstop.
- **`ROUTE_COLORS[type]` reads inherited properties.** `type = "constructor"` returns a function
  rather than falling back to the default colour. Not injectable — the result cannot contain a quote
  — but it produces broken CSS. `Object.hasOwn` or a null-prototype map fixes it.

#### Tests added

`PollingIntervalTests` and `CustomSourceSecretExposureTests` — both dependency-free. The second
guards the *shape* of `CustomSourceResponse` rather than a call site, because the way that regresses
is someone adding `AuthConfig` back for the editor's convenience.

#### Verified clean this round

XXE (`XDocument.Parse` defaults to `DtdProcessing.Prohibit`), zip slip (nothing extracts to a path),
IDOR (every business-manager read filters on `UserId`), rate-limiter policies (all defined ones are
applied), `ReconciliationManager`'s four transactions (all commit), MAUI fire-and-forget `LoadAsync`
(each self-handles), `RealtimeManager`'s singleton caches (pruned against the active feed set), and
`GtfsParser`'s entry lookup (`ValidateGtfs` and `ParseCsv` both key on `e.Name`, so nested-directory
feeds behave consistently).

#### Still un-audited

`FeedManager`'s import pipeline and bulk-copy reader, `ReconciliationManager`'s matching heuristics
and auto-merge thresholds, the MAUI XAML layer, and the migrations themselves.

#### Postscript — the MAUI lint step is non-blocking

The step added in wave 6 ran for the first time this session and found years of drift, which is what
it was for. Two of the three kinds are fixed and CI confirms they are gone:

- seven files carried a UTF-8 BOM against `charset = utf-8`;
- ten files declared a file-scoped namespace and then indented the body four spaces anyway — the
  shape you get converting `namespace X { … }` to `namespace X;` without reflowing. Six were MAUI
  template boilerplate under `Platforms/`, and two of those are for heads CI has never built.

What surfaced behind them is left: `dotnet format` reports a bounded number of diagnostics per run,
so the first batch masked a second. It wants blank lines between import groups
(`dotnet_separate_import_directive_groups`) across about eight files in `Services/`, `ViewModels/`
and `Helpers/`, plus multi-line expression reflow in `Components/AnimatedBackground.xaml.cs`.

That cannot be converged on by hand — each guess at what the formatter wants costs a CI round trip —
so **the step reports without failing the job**. One `dotnet format GetThere/GetThere.csproj` on a
machine with the MAUI workload fixes all of it in a single commit; delete the `continue-on-error`
straight afterwards. A permanently non-blocking lint step is exactly the gap it was added to close.

---

## Session — August 12, 2026 (continued)

### Audit round 3 — the reconciliation decision core, and the last unread areas

Rounds 1–2 had sampled `ReconciliationManager` and `FeedManager` rather than read them. This round
read the parts that decide things, plus the MAUI XAML layer and the migrations.

#### The significant finding: the matcher ranks on name alone

`FindBestMatch` selects the winning station by name similarity only. Distance is computed but used
solely for the search-radius cutoff — never for ranking. The caller then judges **that one winner**
against `Reconciliation:AutoMergeDistanceMeters`.

With the shipped thresholds (name 0.90, distance 100 m, radius 200 m):

| Candidate | Name | Distance | What happens |
|---|---|---|---|
| Station X | 0.95 | 180 m | Wins on name → caller rejects it as too far → manual review |
| Station Y | 0.93 | 10 m | Met **both** thresholds. Never considered. |

Transit stops share names constantly — both sides of a street, several around one square — so
near-ties in name with large differences in distance are the normal case here. The mild effect is
avoidable manual reconciliation; the harmful one is auto-merging onto the wrong stop of a pair, which
is destructive and silent.

**The widened search radius makes it worse, not better.** `CandidateSearchRadiusFactor = 2.0` is
documented as existing so "a near-miss should surface for manual review rather than never be
considered" — reasonable alone. But widening the candidate set is only safe if ranking accounts for
distance, and it does not: every extra station the wider radius admits is another chance for
something far away to win on name and displace a qualifying candidate. The two decisions are
individually sensible and jointly wrong, so raising the factor to surface more near-misses would
*increase* the number of good matches never considered.

**Not fixed.** Ranking on a combined score changes what every existing feed reconciles to, and
nothing in this container can run a reconciliation to measure that. Documented at the site with the
worked example above.

The spatial grid itself is fine: 0.2° cells with a 3×3 neighbourhood scan vastly over-cover the
200 m search radius, so no candidate is missed by the indexing.

Three more recorded at the same place:

- `RouteTypeMatch` in the returned tuple is always `true` — the loop pre-filters on route type — so
  the "route type mismatch" reason `ComputeAutoMergeVerdict` can render is unreachable for any
  candidate that has a match.
- The `nameScore < 0.3` floor is a fourth threshold that is **not** configurable, unlike the other
  three. Lowering `ManualReviewNameThreshold` beneath it silently does nothing.
- Ties fall to grid-cell order (`>` with no stable secondary key), so re-importing an unchanged feed
  can reconcile differently.

And on `HasRouteOverlap`: every early return is `false`, so a stop with no route data can never match
and is forced to a new station. Correct for a normal GTFS feed; wrong for the Network-completeness
case this codebase supports elsewhere, where it duplicates an operator's whole station set. Which it
should be is a product decision.

#### Fixed

| Area | File(s) | What |
|---|---|---|
| **Untyped ticket template** | `Pages/TicketsPage.xaml` | The ticket-card `DataTemplate` had no `x:DataType`, so its bindings resolved reflectively — a renamed property fails as a blank label, not a build error, and CI compiles this XAML once at the end of the Android build. `VERIFY.md` flagged it by name. All twelve bindings were checked against `WalletTicket` by hand first; CI then confirmed the typed template compiles. |
| **Map escaping** | `wwwroot/map/public.js`, `index.js` | Both `esc()` implementations used a `textContent`/`innerHTML` round-trip, which escapes `<`, `>` and `&` but **not** `"` or `'`. Every use is a text context so nothing was broken — but the admin console's `Shell.esc` does escape them, and assuming the two match while writing into an attribute would introduce an injection. |
| **Route colours** | `wwwroot/route-colors.js` | `ROUTE_COLORS[type]` read inherited properties, so a feed-supplied type of `"constructor"` returned a function that stringified into a `style` attribute instead of falling back to the default. Now `Object.hasOwn`. |
| **Date parsing** | `Managers/FeedManager.cs` | All seven `DateOnly` parses used the ambient culture. `"yyyyMMdd"` is digits-only so separators do not matter, but the culture's *calendar* does — on a non-Gregorian default every service date lands wrong, silently. The rest of the codebase is already explicit about culture; these were the exception. |
| **Bulk timeout** | `Managers/FeedManager.cs` | `BulkCopyTimeout` was hardcoded to 180 while every other long step honours `FeedImport:BulkCommandTimeoutSeconds` (default 600) — so the setting was absent from the longest operation in the pipeline, with a value *lower* than the default it overrides. |

#### Recorded, not changed

`ParseExact` on calendar dates **throws**, while the guard three lines above skips a bad
`exception_type` with a warning, `ParseStops` drops impossible coordinates and counts them, and
`ParseGtfsTimeToSeconds` returns null so the row is skipped. The convention for malformed operator
data is skip-and-log; one unparseable date in `calendar_dates.txt` rejects the whole feed.

Left alone because the obvious fix is worse: the exceptions that matter most say a service does
**not** run on a date, so silently dropping one shows departures for a service that is not running.
Failing loudly beats showing wrong times. Changing it means deciding that explicitly and surfacing a
dropped count the way `droppedStops` already is.

#### Verified clean

**The migrations**, which had never been reviewed, are careful work: `AddCustomSources` drops five
legacy tables with `IF EXISTS` guards throughout, children before parents, removes the orphaned
`Feeds.CustomFeedId` foreign key first because it would otherwise block the parent drop, provides a
real `Down()`, and states plainly that `Down()` will not recreate them and to take a backup if the
historical run log matters.

Also clean: `FeedManager`'s version activation (previous versions deactivated, stale shapes deleted
and the new version activated inside one committed transaction), and the other 11 MAUI XAML files —
`TicketsPage` was the only one with an untyped `DataTemplate`.

#### Still un-audited

`ReconciliationManager`'s spatial grid and `PlaceMatchingManager`; the MAUI page code-behind.

---

## Audit round 4 — tier 2: the custom-source path, mobility, operators, the GTFS parser

Round 3 stopped with "`ReconciliationManager`'s spatial grid and `PlaceMatchingManager`" listed as
un-audited. Both are now read, along with the whole custom-source stack, `MobilityManager`,
`OperatorManager` and the rest of `GtfsParser`.

Verified green on run 43 (`16d9c13`) and run 44.

### The one that takes the process down

`CustomSourceEngine.FlattenXml` recursed once per level of nesting with nothing bounding the depth.
`XmlReaderSettings` has no depth limit to set and `XDocument.Parse` builds its tree iteratively, so
it happily returns a document nested tens of thousands deep — and roughly **40 KB** of XML, far
under the 32 MB response cap, is enough to overflow the stack. A `StackOverflowException` cannot be
caught: the process dies, and because this runs from the poller, one broken operator endpoint stops
every other feed with it.

Capped at 64 levels, matching `JsonDocument`'s own default maximum depth — which is what had been
quietly protecting the JSON path all along.

### One check, five copies, one hole

`GtfsParser.ParseStops`, `GtfsParser.ParseShapes`, both `MobilityManager` upserts and
`CustomHttpSource.ToStops` each carried their own copy of the coordinate guard, each commented as
matching one of the others.

Every comparison with NaN is false. So `lat < -90 || lat > 90` rejects nothing when `lat` is NaN,
and `lat == 0 && lon == 0` does not catch it either. Neither CSV nor JSON can write a non-finite
number — but all five paths reach a text parse, and `double.TryParse` accepts the strings `"NaN"`
and `"Infinity"` against `InvariantCulture`'s symbols. A feed only has to contain three characters
in `stop_lat`.

What that bought: the value passed the guard whose entire purpose is keeping junk out of the feed's
geometry, reached a SQL Server `float` column with no NaN to store it in — so the import failed on a
bulk-copy error naming neither the stop nor the reason — and entered the convex hull that draws the
operator's service area.

`Common/GeoBounds.IsUsable` is now the single definition, called from all five.

### Fixed

| Area | File(s) | What |
|---|---|---|
| **Preview cap ignored** | `Services/CustomSourceEngine.cs` | `ExecuteAsync` returned early for `upload://` requests and never applied its row limit, so `PreviewAsync`'s 200-row cap ran the entire file through mapping and completion, and `MaxRows` never applied to an uploaded file at all. |
| **Non-finite numbers** | `Services/CustomHttpSource.cs` | `Num` returned NaN and Infinity to every caller, not just the coordinate one. `(int?)double.NaN` is 0 under .NET's saturating conversions, so a NaN `LocationType` or `StopSequence` became a meaningful zero. |
| **Negative GTFS times** | `Services/GtfsParser.cs` | `int.TryParse` accepts a leading sign, so `"-05:00:00"` became −18,000 seconds; and `h * 3600` is unchecked `int` arithmetic that wrapped past ~596,000 hours, often into a negative. Widened before multiplying, range-checked after, null otherwise — which is what `FeedManager` already handles by skipping and counting. |
| **Operator map pin** | `Managers/OperatorManager.cs` | Latitude and longitude were two independent unordered `FirstOrDefault` subqueries. For an operator serving more than one station nothing tied them to the same row, so the pin could land at a coordinate belonging to neither — and could move between two identical requests. |
| **Arbitrary truncation** | `Managers/OperatorManager.cs` | `GetStationsAsync` and `GetRoutesAsync` cap at 500 with no `ORDER BY`. `TOP` without one lets SQL Server return any 500 rows, so an operator past the ceiling showed a shifting subset and the rest were unreachable. |
| **Delete loads everything** | `Managers/OperatorManager.cs` | `DeleteAsync` `Include`d all four association collections purely to count them, materialising every route and station association into the change tracker on the path that then refuses the delete. Four `COUNT`s answer the same question. |
| **Culture-sensitive parse** | `Managers/MobilityManager.cs` | `GetDouble` used a bare `double.TryParse`, whose default styles include `AllowThousands` — so under any server culture that groups with a dot, `"45.81"` reads as `4581`. Silently. `CA1305`, the analyzer for exactly this, is off in `.editorconfig`. |
| **Missing range check** | `Managers/MobilityManager.cs` | `UpsertStationsFromRecordsAsync` applied no coordinate check at all before writing, unlike its GBFS sibling — so the mis-parsed value above went straight to the database. |
| **Unclamped cosine** | `Managers/MobilityManager.cs` | `GetStationsAsync` was a fourth copy of the bounding-box arithmetic and the one copy that never got the latitude clamp, so it still divided by `Math.Cos` at the pole and built longitude bounds out of Infinity. Now routed through `BuildQuery`. |
| **Leaked pooled buffer** | `Managers/MobilityManager.cs` | The GBFS `JsonDocument` was never disposed. It rents from the array pool and runs on every poll of every GBFS operator. |
| **Uploads outlive deletes** | `Managers/CustomSourceManager.cs` | Deleting a custom source left its directory behind, so every spreadsheet and PDF uploaded for it stayed on disk indefinitely — unreferenced and unreachable through the API. |
| **Permanent "Running"** | `Managers/CustomSourceManager.cs` | The run row is written as `Running` before the import starts, and a cancellation escaped without touching it. `GetAllAsync` reports the latest run's status, so one abandoned request left the console showing an import in flight for good. |
| **Whole-array copy** | `Managers/CustomSourceManager.cs` | The discovery walker did `EnumerateArray().ToList()` for a count and a first element, at every depth — one `JsonElement` copy per stop, on the response it exists to describe. |
| **Dead loop** | `Services/DocumentTableReader.cs` | A `foreach` over the header record whose body was `_ = header;`. |
| **Wrong lifetime** | `PROJECT.md` | Listed `MobilityManager` as a "singleton + hosted" exception to GetThereAPI's reflection registration. It belongs to TransitInfoAPI, which uses no reflection, and it is `AddScoped`. Following the old line — capturing it from a singleton — captures a scoped `TransitDbContext`. `AGENTS.md` had it right. |

### Recorded, not changed

- **Uploaded `.xlsx` expands without bound.** The upload endpoint caps the *compressed* bytes at
  64 MB, which says nothing about what they expand to, and `XLWorkbook` materialises every sheet —
  not just the first one that gets read. Reaching it needs `CustomSourcesManage`, so it is a
  foot-gun rather than a live vulnerability, and the real fix is a streaming reader rather than a
  size check, because compressed size is not the quantity that matters.
- **A multi-page PDF imports its first page.** The column geometry is derived per page from that
  page's header row, and operator PDFs shift columns or split routes across pages often enough that
  concatenating blind produces plausible rows from the wrong columns. It now warns, so the omission
  is visible in the preview instead of surfacing later as a feed missing four fifths of its stops.
- **Two methods have no callers.** `MobilityManager.GetStationsAsync` and
  `UpsertStationsFromRecordsAsync` — which is exactly why both kept defects their live siblings had
  fixed. Both are corrected rather than deleted; whether to keep them is a roadmap call.
- **Zip entry shadowing.** `ParseCsv` takes the first archive entry whose `Name` matches, so an
  archive containing both `stops.txt` and `sub/stops.txt` parses whichever the zip lists first, and
  `ComputeGtfsSha1` hashes both in an order that ties on `Name` alone.

### Verified clean

~~`CustomSourceEngine`'s SSRF handling, which is thorough: the connect-time guard covers redirects,
and `EnsureCredentialStayedHome` separately refuses a response that arrived on a different host than
the one a credential was attached to — the case `HttpClient` does not cover, because it strips
`Authorization` across origins but not the arbitrary header name the `header` auth mode sets.~~
**Wrong, and wrong in this audit's characteristic way** — see *Correction: a guard that ran after
the thing it guarded against* below. The connect-time guard and `DtdProcessing.Prohibit` stand.

`CustomSourceStorage` resolves and containment-checks every path. `ParseXmlRows` parses with
`DtdProcessing.Prohibit`, so the XXE vector is closed.

### Still un-audited

The MAUI client (~7,400 lines of C# and 3,700 of XAML), the 31 `wwwroot` scripts in logic detail,
the 31 test files read for coverage quality, and `Contracts`/`Entities`/`Mapping`.

---

## Audit round 4 — tier 3: the MAUI client (in progress)

Verified green on run 45 (`06a1fc2`).

### The one that moves someone else's ticket

`PendingImportQueue` and `ImportSyncService` — written up in full at both sites, summarised here.

The queue holds tickets created on the device and not yet accepted by the server. It records **no
owner**, and `FlushAsync` gates only on `IsLoggedInAsync()`. So: a guest imports a ticket on a shared
or second-hand phone; someone else signs in; `LoadTickets` calls `FlushAsync`, which finds a
logged-in user and pushes the first person's ticket — barcode payload included — into the second
person's account. `TicketStore` keys every file by an owner hash precisely to prevent this, and says
so: "a device is not a person."

It is also the only ticket store left in plaintext. `TicketStore` encrypts the identical payloads
with AES-GCM under a `SecureStorage` key, on the stated grounds that a ticket payload is a bearer
credential and `AppDataDirectory` is not proof against a rooted device or an ADB backup. The queue
is also the copy that lives longest — a guest never signs in, so nothing ever drains it.

**Not fixed.** The owner half is a product decision, not a defect with one right answer: a guest's
entries are *meant* to follow the next sign-in — that is the guest-to-account upgrade the path
exists for — and nothing can tell whether the guest and the new account are the same person. The
encryption half has no trade-off, but the MAUI head cannot be built or run in this container, and
lifting `TicketStore`'s key handling somewhere both classes can reach is not a change to make blind.

### A translation the app does not reach

The client ships a **complete** Croatian translation: `AppResources.resx` and `AppResources.hr.resx`
both hold exactly 284 keys, in sync. **83 of those 284 — 29% — are referenced by nothing**, in
either C# or XAML.

That count is net of indirection, which a plain search gets wrong: 87 keys have no literal
occurrence anywhere, but four of them — the `JourneyStatus_*` set — are reached through
`JourneyDetailViewModel`'s `Instance[$"JourneyStatus_{journey.Status}"]`. It is the only interpolated
lookup in the client; `ApiMessageMapper`'s table and `AppShell`'s `TitleKey` values both spell their
keys out, so they were already counted as live.

The rest are not stale leftovers from deleted screens. Ten are the `Error_CouldNotLoad*` family,
and the code that should use them says the English out loud instead:

| Where | What the code says | What the resource says |
|---|---|---|
| `Services/WalletService.cs:36` | `"Could not load wallet"` | `Error_CouldNotLoadWallet` → "Nije moguće učitati novčanik: " |
| `Services/CountryService.cs:35` | `"Could not load countries"` | `Error_CouldNotLoadCountries` → "Nije moguće učitati države: " |

The dialogs are a much narrower problem than the unreferenced-key count suggests, and this entry
said otherwise before it was measured. Counted properly:

| | Sites | Where |
|---|---|---|
| Dialog calls with a hardcoded string | **5** of 27 | `TicketsViewModel` ×4, `ImportTicketViewModel` ×1 |
| Dialog calls fully localized | 22 of 27 | everywhere else, including all 14 in `ProfileViewModel` |
| `ErrorText`/`WarningText` set from a literal | **8** | all 8 in `TicketsViewModel` |
| …set from `LocalizationService` | 19 | everywhere else |

So this is not a client-wide pattern. It is **`TicketsViewModel`**, which is half and half inside one
file: `Common_Offline`, `Tickets_SavedAgo` and `Tickets_PendingNotOpenable` go through
`LocalizationService`, while "Could not load tickets." and every action-sheet label do not.

> An earlier version of this entry claimed 28 hardcoded dialog sites and named `ProfileViewModel` as
> the worst with 14. That was wrong: it counted dialog call sites and asserted what they contained
> without opening them. `ProfileViewModel`'s 14 are all localized. The 87 unreferenced keys and the
> `WalletService`/`CountryService` pairs above were measured and stand.

**Not fixed**, and the reason is specific rather than general caution: the action-sheet *labels* are
also the values the `switch` compares against (`choice == EnterManually`). Localising a label
without localising its comparison breaks the branch **silently** — the sheet still opens, the tap
still dismisses it, and nothing happens. That is a change to make with the app running in front of
you, in a language you can read, which is not available here.

### A reachable stub

`ProfileViewModel.SubmitChangePassword` validates that the new password matches its confirmation and
then shows "Password change is not yet available through the app." It never reads
`SubSettingsCurrentPassword` and never calls anything. The screen's other six strings —
`Profile_SubSettings_CurrentPassword`, `NewPassword`, `ConfirmNewPassword`, `UpdateButton`,
`PasswordSuccess`, `PasswordDesc` — are among the 87 unreferenced keys, translated and waiting.

Honest as stubs go: it says it does nothing rather than claiming success. Listed because the entry
point is reachable from the profile screen.

### Smaller

- **The brand palette is in three places.** `#134E4A` and `#5EEAD4` appear as named statics in
  `ShopViewModel` and again as literals in `PageUtility`, alongside the XAML resource dictionary.
- **`ApiEndpoints.Resolve` catches three exception types** and runs inside a `Lazy<string>`. Anything
  else `OpenAppPackageFileAsync` throws is cached by the `Lazy` and rethrown on every later access,
  so a single unexpected failure at startup leaves the app unable to resolve any backend address.

### Verified clean

`TicketStore`, which is careful work: AES-GCM under a `SecureStorage` key, a fresh nonce per write,
authenticated decryption so a file edited on a rooted device fails rather than deserialising into a
chosen ticket, temp-then-move writes, per-owner directories named by hash rather than by user id,
and a documented reason for *not* clearing on a 401. `AuthService`'s serialised refresh, and its
handling of the case where the server rotated the token but the reply was lost. `ApiEndpoints`
otherwise — addresses come from the packaged `appsettings.json` with the compile-time values only as
a fallback.

`TicketPurchaseViewModel`'s `walletTask.Result` reads like sync-over-async but is not: it follows
`await Task.WhenAll(...)`, so both tasks are already complete and a fault would have thrown at the
await.

---

## Audit round 4 — the `docs/` tree

`docs/` was never in the audit's scope. It was promoted after `PROJECT.md` turned out to claim
`MobilityManager` is a singleton when `Program.cs` registers it `AddScoped` — a document that, if
followed, produces a captive `DbContext`. That was one file out of 26; the question was how much
else was wrong.

### The custom-source subsystem is missing from three references

A feature with **4 database tables, a 12-endpoint controller and ~2,000 lines of service code** —
the one round 4 found a process-killing stack overflow in — appears in `feed-pipeline.md` and
`transitinfodb-rebaseline.md` and **nowhere else**:

| Reference | Coverage |
|---|---|
| `db/transitinfo-schema.md` | 4 of 27 tables undocumented, and all four are the `CustomSource*` set |
| `transitinfo-api/endpoints.md` | 1 of 14 controllers undocumented — `/custom-sources`, the **second-largest** at 12 endpoints |
| `transitinfo-api/architecture.md` | The `Services/` listing named 5 of 11 files; the DI table omitted 9 scoped registrations |

`SecretProtector` — which encrypts an operator's stored credentials — appeared in **no** document at
all outside this changelog.

The architecture doc's DI table is the exact hazard `PROJECT.md` was, and worse for looking
authoritative: two closed lists headed "Scoped" and "Singleton", with `SecretProtector` in neither.
A reader resolving it from a singleton has no way to know from the docs whether that is safe.

**Fixed.** All three references now describe the subsystem, written from the entities, the
controller and `Program.cs` rather than from memory, and every claim spot-checked back against
source — the permission strings against `PermissionKeys`, the enum members against
`CustomSourceEnums`, `Feeds.CustomSourceId` against the entity.

### Why this is worth the pages

The failure mode is not a reader being under-informed. It is a reader being **confidently
misinformed**: a lifetime table with no `SecretProtector` row reads as complete, and a schema
reference listing 23 of 27 tables gives no sign that four are missing. Both were caught only by
diffing the documents against code this audit had already read — which is the cheapest that check
will ever be, and the reason `docs/` moved up the queue rather than staying at the end of it.

### Two references that had fallen behind the code

Coverage was the first check; the second was whether what the documents *say* still matches what the
code *does*. GetThereAPI's references came out clean on coverage — 12 of 12 tables, 9 of 9
controllers, 35 of 35 shared contract types — so the drift is one feature rather than a systemic
habit. Two behavioural gaps did turn up:

- **`getthere-api/architecture.md` described rotation as a read-then-write.** It documents reuse
  detection and the load-bearing ordering correctly, and it records the 2026-07-31 address-check
  removal, but not the 2026-08-10 change that made the revoke a conditional `ExecuteUpdateAsync`
  whose `WHERE` re-asserts `RevokedAt == null && ReplacedByToken == null`. That distinction is the
  whole mechanism: without it two concurrent refreshes both pass the reuse check and both succeed,
  so detection is intact for a *replay* and blind to a *race*. Added, with the `claimed == 0`
  branch and why a race is treated as theft.
- **`transitinfo-api/realtime.md` listed the three workers' default intervals** with no mention of
  the 5-second floor. A configured `0` binds silently and turns the poll loop into a busy spin. Added.

### Verified clean

`GetThereAPI`'s reflection-based manager registration is described accurately, including the trade it
names — that the namespace is load-bearing, so a class dropped into `GetThereAPI.Managers` becomes an
injectable scoped service whether or not that was intended. The reuse-detection ordering note, the
`nvarchar(max)` index bug write-up, and the address-check removal rationale all match the code.

### The admin console I had never counted

`GetThereAPI/wwwroot` is a **second** admin console — 7 scripts, 1,239 lines — separate from
TransitInfoAPI's 25. Round 4's scope said "31 wwwroot scripts"; that was TransitInfoAPI's 25 plus an
assumption, and this set had never been looked at in any round.

**Clean on the question that matters.** `Admin.esc` is the correct five-character implementation —
`& < > " '` through a regex replace — not the `textContent`/`innerHTML` round-trip that rounds 1–3
had to fix in `map/public.js`, which silently passes `"` and `'` through. All 38 `innerHTML`
assignments either build from constants or escape every interpolated value, and the one `href` sink
(`admin.js:325`) reads from a hardcoded navigation table rather than from data.

### A client document that was wrong about encryption

`getthere-client/architecture.md` describes the offline wallet in absolutes:

> "**Everything** is keyed by owner — the `sub` claim, or a generated guest id — because a device is
> not a person, and two accounts on one phone must never see each other's tickets."
>
> "**Files** are AES-GCM encrypted under a key in `SecureStorage` … A barcode payload is a bearer
> credential for travel — whoever renders it rides — so it belongs at the same protection level as
> the tokens."

Both sentences are true of `TicketStore` and false of `PendingImportQueue`, which the same document
lists one table earlier as its sibling — "*`TicketStore`, `PendingImportQueue` — each owns a file and
a write lock*" — with nothing to suggest they differ in anything else. They differ in both things
that paragraph claims.

This is the `PROJECT.md` failure again, on a security property rather than a service lifetime: a
reader checking whether on-device ticket data is protected gets an unambiguous yes, and it is only
true of one of the two stores. Corrected in place, with the consequence spelled out and the reason
it is recorded rather than fixed.

### A document that contradicted its own code

`transitinfo-api/reconciliation.md` stated the ranking rule as settled design:

> "The best candidate is the one with the highest **name score** — not the closest. Distance is a
> filter, not the ranking, because coordinates disagree between operators far more than names do."

Round 3 had already written the opposite into `FindBestMatch`'s `<remarks>`, as a **known defect**
with a worked example: a station scoring 0.95 at 180 m beats one scoring 0.93 at 10 m, and the caller
then rejects the 180 m winner as too far and sends it to manual review, while the 10 m candidate —
which met both thresholds — is discarded unseen.

The rationale in the document is not wrong; the omission is. Both statements were live at once, so a
reader of the reference concluded the ranking was correct by design while a reader of the code
learned it was a defect awaiting measurement. The document now carries the defect, the worked
example, the `CandidateSearchRadiusFactor` interaction that compounds it, and the reason it has not
been changed.

Three smaller behaviours the code documented and the reference did not — `RouteTypeMatch` always
being `true` (making one of `ComputeAutoMergeVerdict`'s rendered reasons unreachable), the 0.3 name
floor being the one threshold that is not configurable, and ties falling to grid-cell order so an
unchanged feed can reconcile differently on re-import — are now in both. As is `HasRouteOverlap`
returning `false` from every early exit, which forces a stops-only "Network-completeness" feed to
duplicate an operator's entire station set on every import.

### Documents describing things that are not there

A mechanical check — every backticked identifier in `docs/` that appears nowhere in the source —
found four real cases and confirmed the rest are deliberate history.

**`shared/contracts.md` documented a deleted subsystem as current.** Its "Map" section, 78 lines,
described six types (`MapStationResponse`, `MapRouteResponse`, `MapMobilityStationResponse`,
`MapVehicleResponse`, `MapDepartureResponse`, `MapOperatorResponse`) with full property tables,
cited `Contracts/MapContract.cs`, and gave the rationale "*GetThereAPI proxies TransitInfoAPI and
re-maps into these*". None of the six exist; neither does the file; and the proxy was deleted on
2026-08-02. The section was listed in the document's own index as live.

What makes this worth recording is that the deletion was done *carefully*.
`map-proxy-migration.md` and `getthere-api/transit-integration.md` both carry full SUPERSEDED
banners explaining what went and why — `transit-integration.md`'s even says "read it as history, not
as a description of running code". The contracts reference, which is the canonical list of shared
DTOs and the one most likely to be consulted, was the file that got missed. Now marked the same way,
with the tables kept as the only written record of the shapes.

**`getthere-client/ticket-import.md` named two things that never existed under those names.**
`TicketPriceConverter` is `PriceCurrencyConverter`, and `MapViewModel` was said to raise
`ModeFilterChanged` — it exposes `MapUrl` and a `LoadMap` command, and its own class comment says
the chips it used to own moved into the page. The link beside it pointed at an anchor that went with
them.

**Confirmed *not* defects**, which is the other half of the check: `feed-pipeline.md` names
`CustomFeedDirectImporter` and says explicitly it is deleted; `transit-integration.md` and
`map-proxy-migration.md` name a dozen removed types under their banners;
`db/transitinfo-schema.md`'s `StationMergeMovedRawStops` is the table for entity
`StationMergeMovedRawStop`; `database-drift.md` names the legacy tables it exists to describe
dropping.

## Audit round 4 — full sweep of the MAUI view models

### Fixed

**`JourneysViewModel.AcceptSuggestion` swallowed its own error.** The non-success branch returns; the
`catch` did not, so it fell through to the `await Load()` at the end of the method — which opens with
`HasError = false` and, on a successful reload, never sets it again. A thrown failure while accepting
a suggestion therefore told the user nothing at all: no error, no suggestion removed, no journey
created. Now returns.

### Recorded

**An entire Shop UI state cannot occur.** `ShopOperator.IsBuyable` is documented as "false when the
operator has no sellable options — the design still lists them, dimmed, as *Timetable only — no
ticketing yet*". It is never false: `BuildDirectory` groups the fare list, a LINQ group always has at
least one element, so `items.Count > 0` holds for every row that can exist. An operator with nothing
to sell produces no group and is absent from the screen entirely — the opposite of the design.

Everything downstream is dead with it: `RowOpacity` is always `1.0`, the muted monogram palette never
applies, `Shop_NoOperators` can never render, and `OpenOperator`'s `!IsBuyable` guard never fires.
Not fixed, because the fix is not in the class: listing a ticketless operator needs the directory
built from an operator list rather than a fare list, and `GET /tickets/options` returns only fares.

**The localized failure strings for login and registration can never display.** Both call sites read

```csharp
ErrorText = ApiMessageMapper.Localize(result.Code, result.Message)
    ?? LocalizationService.Instance["Login_Failed"];   // and Register_Failed
```

`Localize` returns non-nullable `string` — `englishFallback ?? string.Empty` on the unmapped path —
so the `??` can never evaluate. It reads as a safety net and is not one. It is also *harmless*, which
is worth stating precisely: `AuthService` builds every failure through
`OperationResult.Fail(problem ?? "Registration failed")`, so the message is never null and the user
never sees a blank banner. The cost is only that two translated strings are unreachable, and the
server's English wins in every case — the same mechanism as `JourneysViewModel.Fail`, which prefers
`result.Message` over its `fallbackKey` whenever the server said anything at all.

That is most of the answer to why 83 resource keys are unreferenced: the pattern across the client is
to show the server's message and keep the translation as a fallback that rarely fires.

**Two smaller ones.** `ShopViewModel.BuildSubtitle` hardcodes `"{n} fares · from {price}"` in a file
that otherwise goes through `LocalizationService`; and `ShopOperator.Subtitle`'s own doc comment
advertises `"Tram, bus · 4 fares from €0.66"`, a shape the code never produces — it emits
`"{name} · {price}"` or `"{n} fares · from {price}"`, with no transport modes in either.

### Verified clean

`TicketPurchaseViewModel`, which is the client half of the money path: a fresh idempotency key per
selection (so a retry of one intent replays rather than double-charges, and a new intent cannot),
client-side affordability that the server's conditional `UPDATE` enforces independently, and no
balance state carried across the navigation to the ticket. `BaseViewModel`'s `IsOfflineNow` is
correctly documented as advisory — "use it to choose what to *say* about a failure that already
happened, never to decide whether to attempt a request". `RegistrationViewModel`'s validation defers
password rules to Identity rather than reimplementing them.

### The sharp edge of the guest upgrade, marked where it happens

`LoginViewModel.Login` is the call site that pushes a guest's queue into the account that just signed
in. The queue's own notes explain why that can be the wrong account; this now says so at the line
that does it, because that is where someone changing the upgrade will be looking.

### Verified clean in this pass

- **`TicketValidity`** — and it is worth naming, because it already contains the rule the ticket
  detail screens break. It treats `DateTimeKind.Unspecified` as **UTC**, with the reasoning written
  out ("the server stores and compares UTC; treating it as local would shift it by the device's
  offset"), only ever downgrades a status, and never expires a null window. Seven tests pin it.
  `ImportedTicketDetailViewModel.Apply` uses it correctly for the *status* and then calls
  `.ToLocalTime()` on the *dates* four lines later — the same method, two opposite rules.
- **`TicketBarcode.ChooseSymbology`** — refuses to draw a UIC 918-3 payload as Code 128 because
  compressed binary will not round-trip, on the stated grounds that a confident wrong code at a
  barrier is worse than no code. Lives in `GetThereShared` rather than the MAUI project specifically
  so it can be tested.
- **`BarcodeRenderService`** — error correction Q for phone screens, a 4-module quiet zone, PNG
  rather than JPEG because lossy artefacts land on the module boundaries a scanner measures, and a
  pinned-buffer `InstallPixels` whose handle outlives the encode.
- **`WalletTicket`** — projects two contracts that deliberately share no base type, and documents
  the coupling it does create (the two status enums must keep spelling `Active` the same way, or
  `TicketStatusColorConverter` silently leaves a badge unstyled).

### The most-shown error string in the app is untranslated

Every API service catches transport failures the same way:

```csharp
catch (Exception ex)
{
    Trace.WriteLine($"[TicketService] {ex}");
    return OperationResult<T>.Fail("Something went wrong. Check your connection and try again.");
}
```

**20 occurrences** across `ImportedTicketService` (7), `JourneyService` (8), `TicketService` (3) and
`WalletService` (2) — and `AppResources.resx` has **no key for it**, so there is nothing to
translate it to.

This is the sharpest version of the localization finding, and it inverts the usual shape of one. The
translated strings that go unused are the *specific* errors; the string a user actually meets — any
dropped connection, any timeout, any DNS failure, on every screen — is the hardcoded English one.
A Croatian user gets a fully translated app until the moment something goes wrong, which is exactly
when the wording matters.

Not fixed here: adding the key is trivial, but the string is also the wrong *content* in some of
those 20 places — a 500 from the server is not "check your connection" — and deciding what each one
should say is a product call that wants doing once, properly, rather than twenty times mechanically.

### Correcting round 4's own scoping: there *are* silent catch blocks

Round 4's scoping listed "no silently-swallowing catch blocks in any of the four source projects"
among the things it had ruled out. That was wrong, and wrong for a reason worth naming: it came from
a grep for `catch { }` with an *empty* body, which is not the question. The question is which catch
blocks neither log nor rethrow.

Asked properly, that returns **24**. Most are fine — `OperationCanceledException` on a worker's
shutdown path, a `JsonException` that degrades to a default config, a `FileNotFoundException` that
means "first fetch for this feed", and the view-model handlers that call `Fail(ex.Message)` and put
the text on screen. Four are genuinely silent and have consequences:

| Where | What it swallows | What the user sees |
|---|---|---|
| `AuthService.GetFullNameAsync` / `GetEmailAsync` | An unreadable JWT claim | Both return null, and `ProfileViewModel` reads *both null* as "this is a guest" — a signed-in user gets the signed-out profile |
| `LocalizationService`'s indexer | A missing or unreadable resource | The **key name** renders on screen (`Tickets_SavedAgo`), with nothing logged |
| `App.InitializeWindowAsync` | Anything, including DI failing to construct `AppShell` | A signed-in user silently lands on the login shell |
| `ProfileViewModel` history load | Any failure | The transactions list is simply empty |

**Fixed the first.** `AuthService` already logs exactly this failure in `GetOwnerKeyAsync` — "Could
not read the subject claim" — so its two sibling methods reading the *same token* now do too. The
inconsistency was inside one class.

The other three are left as they are: `LocalizationService` returning the key is a deliberate and
common fallback, and `App`'s catch has a real argument for being broad (failing to open *any* window
is worse than opening the wrong one). Both would be better with a log line, which is a judgement for
whoever owns the startup path.

Also fixed while here: **the auth `HttpClient` had no timeout.** `MauiProgram` builds
`AuthService`'s client by hand rather than through the factory, so it kept `HttpClient`'s default of
100 seconds while every other call in the app times out at 30. Login, registration and refresh were
the only operations that waited more than three times as long before admitting the network was gone
— and refresh is the worst of the three, because `AuthenticatedHttpHandler` awaits it mid-request
after a 401, stacking that delay on top of a request the user is already waiting on.

### Changing the language does not change the language

`LocalizationService.SetCulture` raises `CultureChanged`. **Nothing subscribes to it** — the event is
declared, raised, and listened to by no one. That matters because every string in the client resolves
exactly once:

- `TranslateExtension` is a **markup extension**, not a binding. `ProvideValue` returns a plain
  `string` at XAML parse time, so a page keeps whatever language it was constructed in.
- `AppShell` reads its tab titles once in `BuildNavigation`, called from a constructor that runs once
  — the shell is registered `AddSingleton`.

So `ProfileViewModel.SelectLanguage` sets the culture and calls `App.GoToApp()`, evidently meaning to
rebuild the UI in the new language. `GoToApp` does
`Windows[0].Page = Services.GetRequiredService<AppShell>()`, which resolves **the same singleton
instance already on screen**. The tab bar and the visible page do not change. Pages are transient, so
navigating somewhere new afterwards *does* pick the new culture up — which is what makes this present
as intermittent rather than plainly broken.

Documented at both sites rather than fixed: the repair is a design choice, not a line. Subscribe and
rebuild the shell; or register `AppShell` transient so `GoToApp` constructs a fresh one; or make
`TranslateExtension` return a binding that tracks the event — only the last also updates a page
already on screen. None of the three can be judged without the app running.

### Three more members that exist and do nothing

- **`AppShell.NavItem.DesktopOnly`** — declared, defaulted false, never set true, and filtered on.
  It described a Settings destination that was desktop-only because the phone frames had no room for
  a fifth tab. Settings was folded into Profile → Account, so the destination is gone, the filter
  removes nothing, and the desktop and phone navigation lists are identical.
- **`AppShell.UpdateProfileIcon`** — public, carefully written (it looks the tab up by route, because
  the tree is a `TabBar` on phones and `FlyoutItem`s on desktop), given its own section in
  `getthere-client/architecture.md` — and called by nothing. Nor could it be:
  `ProfilePage.OnAvatarClicked` offers "Take Photo" / "Upload" and answers either with
  `Profile_PhotoResult`, whose text is *"Camera/Gallery integration would go here."*
- **`ProfilePage.OnRequestedThemeChanged`** — subscribed in the constructor, unsubscribed on
  disappearing, and its body computes `isDark` and discards it under a comment explaining that icons
  use `AppThemeBinding` so no manual update is needed. The handler is the leftover of the manual
  update that is no longer done.

### Where the untranslated UI actually is

Counting `{localization:Translate}` against hardcoded literals per screen resolves the localization
thread into something bounded:

| Page | Translated | Hardcoded |
|---|---|---|
| `ProfilePage` | 24 | 0 |
| `TicketsPage` | 19 | 0 |
| `RegistrationPage` | 15 | 0 |
| `LoginPage` | 13 | 0 |
| `ShopPage` | 9 | 0 |
| `JourneyDetailPage` / `TicketDetailPage` | 8 | 0 |
| `TicketPurchasePage` | 7 | 0 |
| `ImportedTicketDetailPage` | 4 | 0 |
| `MapPage` | 0 | 0 |
| **`ImportTicketPage`** | **1** | **17** |

Every content page is fully translated except one. `MapPage`'s zero is correct — it is a WebView and
the page it loads labels itself from the `lang` parameter in its URL.

`ImportTicketPage` is the outlier, and it is not an isolated file: the same flow's view models carry
the rest of it — `TicketsViewModel`'s four hardcoded action sheets and eight hardcoded `ErrorText`
assignments, and `ImportTicketViewModel`'s prompt and `SummariseFor` text. That is the ticket-import
flow, end to end, and it is the newest feature in the client. It reads as a feature built after the
localization pass and never put through it.

So the accurate statement of this whole thread, replacing the vaguer ones above:

1. **The language switch does not work at all** (`CultureChanged` has no subscribers, the shell is a
   singleton, `TranslateExtension` is not a binding). This is the one that matters.
2. **One flow was never localized** — ticket import, ~26 strings across three files.
3. **The generic transport error is hardcoded** in all 20 service catch blocks, with no key for it.
4. 83 unused keys are a *consequence* of 1–3 rather than a defect in their own right.

None of it is fixed. Fixing 2 and 3 without 1 would produce a fully translated app that still cannot
be switched into Croatian.

## Audit round 4 — the map scripts

### Fixed: a stale response could paint the map with the wrong region

Both maps — `map/public.js` (anonymous) and `map/index.js` (admin) — reload three GeoJSON layers on
`moveend`, debounced 500 ms. The debounce stops a burst *during* one drag; it orders nothing
*between* drags. Two pans 600 ms apart put two sets of requests in flight, and the map keeps
whichever **responds** last.

That is not a rare interleaving, it is the common case with a predictable direction: the dense-area
response is the slow one, so panning from a city to open country reliably repaints the city's
stations over the view the user has already moved to. Nothing corrects it until the next pan.

Each map now aborts its previous set before issuing a new one, and treats `AbortError` as "superseded
by a newer view" rather than a failure. That is not a new idea in this codebase — `public.js`'s own
`runSearch`, 600 lines further down, has done exactly this since it was written:

```js
if (searchAbort) searchAbort.abort();
searchAbort = new AbortController();
```

The pattern was present and simply not applied to the layer fetches.

Verified with `node --check` on both files. Worth stating plainly: **CI has no JavaScript step at
all** — no lint, no syntax check, no tests — so 6,013 lines of `wwwroot` script are covered by
nothing. A syntax error in any of them ships.

### Verified clean

`admin/admin-auth.js`, which is the console's credential path and has clearly had attention: it reads
the token per request rather than baking it in at load, shares one in-flight refresh across
concurrent callers (with the reasoning recorded — a second parallel refresh presents a replayed token
and trips reuse detection, revoking every session the operator has), copies the caller's options
rather than mutating them, uses a `Headers` instance so a caller passing one is not silently ignored,
and refuses to attach the token to anything that is not same-origin.

### CI never looked at any JavaScript

The workflow builds four C# projects with `-warnaserror`, runs the test suite against a real SQL
Server, and runs `dotnet format` over five projects. It touched **no JavaScript** — no lint, no
syntax check, no tests.

That leaves **7,252 lines** across the two `wwwroot` trees (6,013 in TransitInfoAPI, 1,239 in
GetThereAPI) as the only code in the repository that can ship broken with a green build. A missing
brace in `map/public.js` is a blank map for every anonymous visitor and a passing run.

Added a `node --check` step to the `build` job. It parses without executing, so it needs no
dependencies, no browser and no network — and it catches exactly the class of failure that currently
has no guard anywhere. It is not a linter and does not pretend to be one; a real one would want
`eslint` and a config, which is a larger decision. All 32 files pass today, and the step's script was
run verbatim here before committing, exit code and glob included.

This is the same shape as the gap round 1 closed by adding `workflow_dispatch`: not a defect in any
one file, but a blind spot in what the pipeline is willing to look at.

## Audit round 4 — the test suite, read for what it does not cover

252 test methods across 31 files, and they are good tests — the ones this audit has leaned on
(`TicketValidityTests`, `GtfsParserTests`, `PurchaseFlowTests`) are precise about *why* each case
exists. The question here is the other one: what has no test at all.

### The integration host covers one service

`ApiFactory` is `WebApplicationFactory<GetThereAPI.ApiEntryPoint>`. **There is no equivalent for
TransitInfoAPI**, so nothing exercises its HTTP surface, and its coverage is exactly the pure-logic
tests — the GTFS parser, the custom-source engine, the document completer, the reconciliation grid,
Levenshtein, `PollingInterval`, feed storage, the SSRF guard and the secret-exposure check.

Everything else in that service is untouched by any test:

| Not exercised by any test | Lines |
|---|---|
| `FeedManager` — the whole import pipeline, largest file in the project | 1,494 |
| `CustomSourceManager` | 554 |
| `RealtimeManager` — the singleton holding live vehicle state | 500 |
| `StationManager` — the anonymous-facing search and GeoJSON reads | 452 |
| `MobilityManager`, `PlaceMatchingManager`, `OperatorManager`, `ScheduleManager` | 1,443 |
| `RouteManager`, `OnestopIdManager`, `PlaceManager`, `CountryManager`, `GeoCountryDetector`, `GeoUtils` | 569 |
| `FeedPollingWorker`, `RealtimePollingWorker`, `MobilityPollingWorker` | 312 |

**5,012 lines of manager code and 312 of worker code**, in the service where rounds 1–4 found most of
the defects.

### The money asymmetry

This is the one worth acting on. The suite tests taking money *out* of a wallet thoroughly —
`PurchaseFlowTests`, `PurchaseReconciliationTests` and a 213-line `MoneyPathFixture` that runs
against real SQL Server, covering the conditional debit, idempotent replay, the refund compensation
and the reconciliation sweep.

**Putting money in has no test at all.** `WalletManager` (130 lines — `TopUpAsync`, balance, wallet
creation) is named by nothing. `/wallet` does appear in `HttpSurfaceTests`, but only in
`Anonymous_callers_are_challenged`, which asserts a 401 — authorization rejects the request before
the controller runs, so the manager is never reached. `AdminManager` (305 lines, including the
`volume / sold` money aggregation) is in the same position via `/admin/stats`.

So the credit path and the money-reporting path are both unexercised, while the debit path beside
them is the best-covered code in the repository.

### Caveat, stated because the distinction matters

This measures *reachability*, not line coverage: "no test names it and no integration host reaches
it". A manager could still be indirectly exercised through a path I have not traced. What is not in
doubt is the structural gap — one service has an integration host and the other does not, and the
money-in path has neither a unit test nor a reachable endpoint test.

## Audit round 4 — the admin console's Content-Security-Policy

The two policies this service sends are not equally strict, and the weaker one guards the more
valuable page.

| | `script-src` |
|---|---|
| Public map (`/map`) | `'self'` |
| **Admin console (`/admin`)** | **`'self' 'unsafe-inline' https://cdn.jsdelivr.net`** |

**`'unsafe-inline'` removes the protection CSP exists to give.** It is there because the console uses
**66 inline `onclick` handlers** across its page scripts. The console renders operator- and
feed-supplied strings throughout — names, URLs, licence text — which is precisely why `Shell.esc` and
`Shell.safeUrl` were written and consolidated. Those two functions are currently the *entire*
defence: the policy that would normally catch anything they miss is disabled for the one directive
that would have caught it.

**Bootstrap is loaded from a third-party CDN with no Subresource Integrity.** Every admin page carries

```html
<script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
```

and **no page in the console uses `integrity=`**. The version is pinned in the path, which jsdelivr
serves immutably, so this is not about version drift — it is that a compromise of that origin
executes arbitrary script inside a page holding an operator's bearer token in `sessionStorage`.

The map's policy is the useful comparison: `script-src 'self'`, no CDN, no inline. Strict is
evidently achievable in this codebase; the console is where it was not achieved.

**Not fixed, and both reasons are about not guessing.** Adding SRI needs the real digest for that
exact asset, and inventing one breaks every admin page — it has to come from the file, fetched or
vendored. Dropping `'unsafe-inline'` means converting 66 handlers to `addEventListener` across a
dozen files that have no behavioural tests at all; the syntax check added earlier this round would
confirm they still parse, and nothing would confirm they still work.

The cheapest real fix is probably neither: vendoring `bootstrap.bundle.min.js` into `wwwroot` removes
the CDN and the SRI question together, and leaves only the inline-handler work behind `'unsafe-inline'`.

## Audit round 4 — the declarative layer

Entities, contracts and EF configuration. Three checks, all against the generated schema rather than
the source, because the source does not say what the database does.

**Decimal precision: 14 of 14 configured.** Every `decimal` property in either service has an explicit
`HasPrecision` — money at `(18,2)`, and the reconciliation scores at `(5,4)` with distances at
`(14,4)`, which are the right shapes for a 0..1 score and a metre measurement. Nothing falls back to
a provider default.

**Delete behaviour: 62 of 64 foreign keys are `Restrict`, and the two exceptions are the right two.**

| Schema | Restrict | SetNull | Cascade |
|---|---|---|---|
| GetThereAPI | 18 | 4 | **0** |
| TransitInfoAPI | 42 | 0 | **2** |

Both cascades are deliberate: `AspNetRoleClaims → AspNetRoles`, which is Identity's own default and
correct — a deleted role should not leave orphaned claims — and `RefreshTokens → AspNetUsers`, added
by a migration named for it. Nothing in the domain cascades: deleting an operator, a feed, a wallet
or a purchase is refused rather than allowed to take history with it, which is what the managers'
explicit ordered deletes exist to work with.

`AppDbContext` gets there by convention rather than per-relationship — a loop over
`entityType.GetForeignKeys()` setting `Restrict` on all of them, with the four `SetNull` cases
configured afterwards to override it. Worth knowing before reading that file: twelve `HasOne` chains
carry no `OnDelete` call and are nonetheless `Restrict` in the schema.

**Enum storage** is `EnumToStringConverter` applied by the same convention loop, so an enum is a
readable string in the database rather than an ordinal that shifts when a member is inserted.

### A note on method

Two of the three checks above started as source-level greps that produced alarming numbers — "12
relationships with no delete behaviour", "24 catch blocks that swallow". One was a false alarm and
one was real. The difference was only ever visible by going to the artefact rather than the source:
the migrations for the schema, and the catch bodies for the logging. Recorded here because the same
mistake recurred several times in this round, and the correction is cheap when it is remembered.

### Fixed: a bulk approve was an unbounded lock storm

`reconciliation.page.js`'s `batchApprove` ran `Promise.all` over every selected candidate, issuing
all of them at once. An approval is not a read: `ReconciliationManager.ApproveCandidateAsync` opens
its own transaction and merges stations, moving `RawStops` and `CanonicalStationOperators` rows. The
console pages 50 at a time, so a select-all is fifty concurrent write transactions contending for the
same canonical rows.

SQL Server resolves that by choosing deadlock victims. Those arrive back as `r.ok === false` and are
counted by the existing `.catch(() => false)` as ordinary failures — so a deadlocked batch is
indistinguishable from one the server legitimately rejected, and the operator is told "31 of 50
approved, 19 failed" with no way to tell which kind of failure it was.

There is no batch endpoint to defer to; the API exposes only `POST /reconciliation/{id}/approve`. So
the fix is a client-side window: a `mapLimit` helper running four at a time, preserving result order
so the existing success/failure tally still lines up with the ids.

Verified beyond the syntax check — the helper was executed here over 23 items with an instrumented
worker: peak concurrency 4, output order identical to input order.

The same `Promise.all`-over-a-selection shape is worth looking for elsewhere in the console; this is
the only one that drives a merge, which is why it is the one that mattered.

### Fixed: the post-action refresh failed silently

`reconciliation-map.page.js` calls `reFetchCandidate` / `reFetchStationTimeline` immediately after an
approve or reject has **already succeeded** — they are the refresh that shows the operator the new
state. Both ended `.catch(() => {})`.

So a failed refresh left the sidebar showing the pre-action state, with the action already applied on
the server. The obvious response to a panel that did not change is to click the button again.

What makes it worth calling out rather than filing under "another empty catch": both chains
*construct* distinct errors — `throw new Error('Station not found')`,
`throw new Error('Failed to load candidates')` — purely to discard them one line later. The
information existed and was deliberately dropped. Both now surface through `alert`, which is what the
other five error paths on this page already use, and say which half failed: the action succeeded, the
panel did not refresh, reload.

### The parallel-write sweep, closed

`batchApprove` was flagged as possibly one instance of a pattern. It is not — it was the only one.
Every other `Promise.all` in either console fans out **reads**: dashboard KPIs, map layers,
related-entity lookups. The one that looked riskiest, the overview's per-feed version fetch, is
already `.slice(0, 20)`.

### Recorded, not fixed

`reFetchStationTimeline` and the `map.on('load')` handler in the same file are near-identical
35-line duplicates — same two fetches, same marker plotting, same bounds fitting. Only the second
carries the explanatory comments. A change to one will not reach the other, which is the ordinary way
these drift; left alone because collapsing them is a refactor rather than a fix, on a page with no
test of any kind.

### A stored credential can be replaced but not removed

The custom-source editor handles the credential correctly on the way in — it never populates the
field from the model, because the server stopped returning `AuthConfig` and now sends only a
`HasAuth` boolean, and the help text says what blank means: *"A credential is stored. Leave blank to
keep it, or type a new one to replace it."*

On the way out it collapses two states into one. `collect()` sends
`document.getElementById('fAuth').value.trim() || null`, so a blank box becomes `null` — and the API
distinguishes:

- `null` → `UpdateAsync` skips the assignment; the stored credential stays.
- `""` → assigned. `SecretProtector.Protect` passes blank through unchanged (encrypting "no
  credential" would only make it indistinguishable from one), and `HasAuth` is
  `!string.IsNullOrWhiteSpace(cs.AuthConfig)` — so an empty string genuinely clears it.

The clearing path exists in the API and is unreachable from the console. Removing a stored credential
is exactly what rotating away from an integration requires, and today it needs a direct `PUT`.

Recorded rather than fixed: the repair is a deliberate control — a "Clear stored credential" checkbox
or button — not a change to how the blank box is read, since blank already means *keep* and the
field's own help text promises that.

Confirmed while checking this that the endpoints reference added earlier this round states the
`null` / `""` distinction correctly.

### A reset button that cannot do what it says

`shape-editor.page.js` offers *"Reset to auto-generated shape? This will discard your manual edits."*
and implements it as `location.reload()`.

Reload re-fetches `GET /routes/{id}/shape` — the **saved** shape. So the control has two behaviours
and the label describes neither accurately:

- **Before a save**, it discards unsaved edits. Fine, and roughly what the message says.
- **After a save**, it reloads the same manual shape it claims to be replacing. Nothing resets.

And the auto-generated geometry is not recoverable at that point. The `PUT` overwrote it, and
`AutoGenerateShapesIfMissing` lives inside `FeedManager`'s import path with nothing in front of it —
`RoutesController` exposes only `GET` and `PUT` for a shape. There is no regenerate endpoint, so
"reset to auto-generated" is not implementable from this page at all.

The honest version of the button is *"revert to the shape as loaded"*, and the file is already set up
for it and does not use it: **`originalGeometry` is written on load and again after every successful
save, and read nowhere.** Two assignments, no reads — it is the state a real revert needs.

Annotated rather than changed: making the behaviour match the label needs a regenerate endpoint, and
making the label match the behaviour is a wording decision on a destructive control. Both are calls
for whoever owns the page.

### Verified clean

`shape-editor.page.js`'s save path itself — disables the button while in flight, restores it in
`finally`, and updates `originalGeometry` only after the server confirms. `operators.page.js`'s
delete confirms by name with "This action cannot be undone", which matches the API: `DeleteAsync`
refuses while any agency, feed, route or station association remains.

### Fixed: a blank operator dropdown with no explanation

`feeds.page.js` carried seven empty catches, the most of any file here. Five are defensible and are
left alone: four sit inside the import log-polling loop, where a failed tick is retried a second
later and the import is observable elsewhere, and one is MapLibre's idiomatic remove-if-present.

The other two are the same five lines duplicated in `editFeed` and `showAddModal`:

```js
try {
  const r = await fetch(BASE + '/operators');
  const j = await r.json();
  _operators = j.data || [];
} catch(e) {}
```

When `/operators` fails, `_operators` stays `[]` and the modal renders an empty operator `<select>`.
On the add path that is a dead end — a feed cannot be created without an operator — and the console
said nothing about why the list was blank. Both now report through the page's existing `showError`.

### Verified clean across the remaining console pages

- **Every `DELETE` in both consoles is behind a `confirm()`.** Checked mechanically rather than by
  eye: no destructive verb appears without one in the preceding lines.
- `operators.page.js` confirms by name and states the action cannot be undone, which the API backs —
  `DeleteAsync` refuses while any agency, feed, route or station association remains.

## Audit round 4 — the console's own CSP breaks one of its pages

### The shape editor cannot draw

`/admin/shape-editor.html` loads its drawing library from Mapbox:

```html
<link rel="stylesheet" href="https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-draw/v1.4.3/mapbox-gl-draw.css">
<script src="https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-draw/v1.4.3/mapbox-gl-draw.js"></script>
```

The CSP this service sends for `/admin` is:

```
script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net
style-src  'self' 'unsafe-inline' https://cdn.jsdelivr.net
```

**`api.mapbox.com` is in neither.** The script is blocked, so `MapboxDraw` is never defined — and
`shape-editor.page.js:142` is `draw = new MapboxDraw({ … })`, which then throws `ReferenceError`.
The page's entire purpose is drawing route geometry, and it cannot, in any browser that enforces the
policy the same server sets.

This is not a subtle interaction. It is one file asking for an origin another file forbids, and
nothing in the build or the test suite looks at either — which is the same gap that let the missing
JavaScript check sit unnoticed.

### The console's fonts have never loaded either

Both `admin/style.css` files — TransitInfoAPI's and GetThereAPI's, 25 pages each — open with

```css
@import url('https://fonts.googleapis.com/css2?family=Open+Sans:…&family=IBM+Plex+Mono:…');
```

`style-src` does not list `fonts.googleapis.com`, so the stylesheet is blocked; `font-src` does not
list `fonts.gstatic.com`, so the files it would have pulled are blocked too. Open Sans and IBM Plex
Mono have never rendered on any admin page — every one has been falling through to the next entry in
its font stack.

### One root cause, three symptoms

The console depends on **three** external origins and its CSP permits **one**:

| Origin | Used for | Allowed? |
|---|---|---|
| `cdn.jsdelivr.net` | Bootstrap | yes — but with no SRI |
| `api.mapbox.com` | mapbox-gl-draw | **no** — shape editor broken |
| `fonts.googleapis.com` / `gstatic.com` | web fonts | **no** — fonts never load |

Vendoring all three into `wwwroot` fixes every row at once: the shape editor starts working, the
fonts appear, the SRI question disappears with the CDN, and `script-src`/`style-src` can drop to
`'self'` — leaving only the 66 inline handlers behind `'unsafe-inline'`.

Not done here. It means fetching three assets and committing them, and I can neither verify their
contents from this container nor test the pages afterwards; guessing at vendored library bytes is a
worse failure than the one being fixed.

### The root-level markdown

`VERIFY.md` (215 lines) opens with its own disposal condition:

> **Disposable.** This exists to get one branch verified. Delete it once the branch is merged — it is
> not part of the permanent docs.

That branch is `claude/maui-ui-to-transitinfo-map-9cj09m`, and it merged — `12e5982`, "Merge pull
request #72". The condition is met and the file is still here, which is how it has started to drift:
it states `.resx` parity at **281 keys in both cultures**, and both files now hold **284**.

Left in place. It is a self-declared temporary file whose deletion its own text authorises, but it
also contains the only written record of a set of manual verification steps — the map checks, the
scanner checks, the two-service-independence checks — that nothing else in the repository covers.
Deleting it is a judgement about whether that record is worth keeping somewhere permanent, which is
not mine to make; the drift is worth knowing about either way.

`ROADMAP.md`, `README.md` and `.opencode/` carry no claims that this audit contradicts.

### Configuration

`appsettings.json` in both services, `launchSettings.json`, `Directory.Packages.props` and every
`.csproj` read. Nothing outstanding. Worth recording two things that are *right*, because both are
the kind of thing that is usually wrong:

- `Jwt:Key` ships as the placeholder `CHANGE-ME`, and **both** `Program.cs` files refuse to start on
  it, on empty, or on anything under 32 bytes — with the `dotnet user-secrets` command in the
  exception message.
- Package versions are centralised in `Directory.Packages.props`, so the two services and the test
  project cannot drift onto different versions of the same dependency.

---

## Review round — three comments on PR #73

All three were right. Two of them are defects this audit introduced, and one is a correction of a
claim made in this changelog.

### Correction: a guard that ran after the thing it guarded against

`CustomSourceEngine.EnsureCredentialStayedHome` compared the host it requested with
`response.RequestMessage.RequestUri.Host` and refused the response if they differed. The **Verified
clean** section above described that as closing "the case `HttpClient` does not cover". The
comparison was right; the conclusion was not.

`AllowAutoRedirect` was never set anywhere in either service — `ConfigureFeedHandler` sets only
`ConnectCallback` — so it defaulted to `true`. The redirect therefore happened *inside* `SendAsync`,
before any code here saw a response. `HttpClient` strips `Authorization` across origins, but it
strips nothing else, and `ApplyAuth`'s `header` mode sets an arbitrary header name — an API key, in
practice. By the time the check ran, the key was already at the redirect target. Refusing the body
stopped bad data being ingested. It could not un-send the credential.

The fix takes the redirect away from the handler:

- A new `"customsource"` named client with `AllowAutoRedirect = false`, separate from `"gtfs"`
  because GTFS archive URLs legitimately redirect and have no credential to lose.
- `SendFollowingRedirectsAsync` issues each hop itself and decides *before* sending: an off-host
  redirect on a credentialed source is refused and the credential never goes out; so is an
  https → http downgrade on the same host. An unauthenticated source may redirect anywhere.
- Every hop re-runs the SSRF check, and the chain stops at five hops.
- The send switched to `HttpCompletionOption.ResponseHeadersRead`, so `ReadCappedAsync`'s 32 MB
  ceiling is enforced while the body streams rather than after `HttpClient` has already buffered it
  — the same mistake in miniature.

Six tests in `CustomSourceRedirectTests` assert on **what was sent**, not on what came back. That
distinction is the whole point: a test that only inspects the response passes against both the old
code and the new one. One of them pins the client name, because reverting to `"gtfs"` would compile,
pass everything else, and silently restore the defect.

How this got into the changelog as "verified clean" is worth stating plainly, because it is the same
failure mode recorded twice already in this document: I read the method, found its stated reasoning
sound, and wrote that down — without asking when it runs relative to the leak it describes. Accepting
a piece of code's own account of itself is not verification.

### A doc comment split across the wrong member

`TicketUploadManager`'s "Deletes uploads the user never turned into a ticket…" summary was left
orphaned by `655e04e`, which inserted `PurgeBatchSize` and its own `<summary>` between that comment
and `PurgeAbandonedAsync`. Two `<summary>` blocks in a row meant the method's description documented
the constant. Introduced by this audit; split back apart.

### The shape editor's reset button now does what it says

`resetShape` called `location.reload()` under the label "Reset to auto". Documented during the sweep
as not implementable — there is no regenerate endpoint, `RoutesController` exposes only GET and PUT —
with the note that the honest version was "revert to the shape as loaded", which the file already
tracked in `originalGeometry` and read nowhere.

Implemented: the button reads `Revert edits`, the confirmation says "Revert to the last saved shape?",
and it restores `originalGeometry` in place, deep-copied so MapboxDraw editing the restored line
cannot mutate the copy. Reverting in place also keeps the route name, badge and stop markers that a
reload would have re-fetched. A true "reset to auto" still needs a regenerate endpoint.

Documenting a defect and leaving the misleading label in place was the wrong call. The write-up
identified the fix and then stopped short of it; a reviewer had to point at the note to get it done.
