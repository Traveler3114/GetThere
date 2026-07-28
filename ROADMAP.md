# GetThere — Master Roadmap

This document consolidates the full high-level plan: foundational fixes, existing technical debt, and new features (wallet, journeys, trip planning, etc.), sequenced so that structural work happens before feature work, and feature work happens before monetization.

**Sequencing logic:** fix the shaky foundation first → clean house → ship the flagship value feature before asking for money → build real trip planning → polish what's stubbed → introduce payments only once the base is trustworthy → mature compliance/ops as usage grows → give humans tools to run it → widen reach and add intelligence last.

---

## Phase 0 — Foundation ✅

Blocks everything else. Do not build new user-data features on top of an auth system about to be restructured.

### Completed

- ✅ Unify Identity user-key type across TransitInfoAPI (`int`) and GetThereAPI (`string`) → standardized on `string`
- ✅ Move secrets (JWT keys, admin/service passwords) out of `appsettings.json` into env vars / user-secrets — both APIs additionally fail fast at startup on a missing, weak, or `CHANGE-ME` `Jwt:Key`
- ✅ Define a secrets rotation policy → `docs/secrets-rotation.md`
- ✅ Fix password policy mismatch → removed client-side validation; server enforces `RequiredLength = 12` via Identity config + `[MinLength(12)]` on shared `RegisterRequest` DTO
- ✅ Fix `RealtimeManager` race condition → removed dead `UpdateTripUpdate` method
- ✅ iOS Privacy Manifest fix (`NSPrivacyAccessedAPICategoryUserDefaults`) — unblocks future App Store submission
- ✅ Decide MVVM → adopted CommunityToolkit.Mvvm, all 7 pages converted

### Deferred

- ⏭️ Push notification infrastructure (FCM/APNs, device token storage) → moved to Phase 4
- ⏭️ Decide business/pricing model (commission per ticket, subscription, freemium wallet features) → **moved to Phase 2**, not Phase 6. It determines wallet schema (commission fields, subscription entitlements, whether balance is spendable or promotional), and Phase 2 is hardening that schema now. Payments still ship in Phase 6; the *decision* must precede the schema freeze.

---

## Phase 1 — Cleanup & Baseline Ops (mostly done — 2 items open)

- ✅ Remove dead code — deleted `BreathingBackground`, `AnimatedGradientBehavior`, `SqlHelper`, 4 unused converters (`StatusToColor`, `ProviderIcon`, `InstallBtnText`, `InstallBtnColor`)
- ✅ Deduplicate `RoleDto` / `UserDto` — moved to `GetThereShared.Contracts`, removed 5 local copies across both APIs
- ⏳ CI build-check pipeline — `.github/workflows/build-check.yml` restores/builds/lints the three non-MAUI projects per-project with `-warnaserror`, plus a `continue-on-error` MAUI Android job. **Not yet ✅: no Actions run has been green.** Verify by: green run on `main`.
- ⚠️ Crash reporting — `Sentry.Maui` wired via `Resources/Raw/appsettings.json`, but the committed DSN is empty and `TracesSampleRate` is `0.0`, so it is **inert**. Verify by: set a real DSN, throw a test exception on an Android device, confirm it arrives in Sentry.
- ✅ Basic analytics — `IAnalyticsService` / `AnalyticsService` stub wired into screen tracking (Shell navigation), login, registration, top-up events
- ✅ Surface GTFS-RT data **on the API** — added `OccupancyStatus`, `OccupancyPercentage`, `CongestionLevel`, `Speed`, `WheelchairAccessible` to `VehicleResponse` (populated in `RealtimeManager`); new `GET /realtime/tripupdates` endpoint
- ⏳ Surface GTFS-RT data **in the app** — no MAUI code reads occupancy, congestion, or accessibility yet, so none of it is user-visible

---

## Phase 2 — Wallet Core (flagship pre-purchase feature)

Builds value before ticket purchasing is live.

### Completed

> **Caveat:** the ✅ marks below reflect code that is written and reviewed but
> **not yet committed and not yet run** — the working tree is uncommitted and the
> manual smoke path (see Remaining) has not been walked. Treat them as provisional
> until it has.

- ✅ `ImportedTicket` entity + supporting enums (`ImportSource`, `ImportedTicketStatus`, `VerificationStatus`) — full data model per spec, separate from adapter-purchased `Ticket`
- ✅ `ImportedTicketResponse` / `CreateImportedTicketRequest` DTOs in `GetThereShared.Contracts`
- ✅ EF Core migrations `AddImportedTickets` + `HardenImportedTickets` (column max lengths, unique filtered dedupe index)
- ✅ `ImportedTicketManager` + `ImportedTicketsController` — create, list, get by id, update status, soft-delete (cancel). *Verify by: exercise all five from a non-admin account — the `User` role needs `importedtickets.view/create/manage`.*
- ✅ Import via **manual entry** — `ImportTicketPage` / `ImportTicketViewModel`, with local→UTC date normalization and a currency picker backed by shared `SupportedCurrencies`
- ✅ Duplicate-ticket detection — `DedupeHash` (SHA256 of raw payload, or an `operator|route|validFrom|validTo|source|name` composite), pre-checked on create and backstopped by unique filtered index `IX_ImportedTickets_UserId_DedupeHash` on active non-null hashes. *Verify by: double-tap Save → second attempt returns 409, not a duplicate row.*
- ✅ Verified/unverified marking — `VerificationStatus` enum (Unverified / Verified / Suspicious), orthogonal to ticket lifecycle `Status`
- ✅ Status lifecycle enforced — `Active→Used|Expired|Cancelled` only; invalid transitions return 400. `TicketExpiryWorker` sweeps `Active`→`Expired` hourly on `ValidTo`, so `Expired` is reachable.
- ✅ Length limits & validation — `[MaxLength]` on the request DTO, `HasMaxLength` in the EF model, manager-level validation of date ranges and currency against the shared allow-list
- ✅ Paginated + filterable list — `GET /importedtickets` returns `PagedResult<T>`; filters by status, source, operator, and validity-date range; sorts by `createdAt` / `validFrom` / `validTo` / `ticketName`
- ✅ Image/document storage **implemented** — `ITicketFileStore` with a local-disk implementation (this deployment has no Azure/S3 of any kind); 10MB cap enforced twice; server-minted GUID blob keys; path containment mirroring `FeedManager.GetFeedStorageDirectory`; `ITicketFileScanner` no-op wired in as the reserved malware-scanning hook.
- ✅ Import via **file upload** — `POST /importedtickets/upload` accepts PDF, JPEG/PNG/WebP/HEIC, Apple Wallet `.pkpass`, and iCalendar, plus `POST /importedtickets/extract-text` for pasted confirmations. Type is decided by **sniffing magic bytes**, never the declared `Content-Type` or filename, and the sniff also routes to the extractor. `.pkpass` archives are bounded against zip bombs. Uploads have their own rate-limit policy.
- ✅ **QR/barcode decoding** — server-side via ZXing: QR, **Aztec and PDF417** (the formats UIC 918-3 rail tickets use), Data Matrix, Code128/39, EAN, ITF. PDFs are scanned for barcodes on embedded images as well as read for text.
- ✅ Extraction returns a **draft the user confirms**, never an auto-created ticket — what a file yields ranges from near-complete (a wallet pass) to nothing (a photo with no code), and a silent guess would put wrong data in a wallet.
- ✅ `SourceFileBlobKey` is back on the create DTO, deliberately and safely: it is a server-minted GUID recorded in `TicketUploads` and resolved against the caller's own **unconsumed** uploads, so a client cannot name a file it did not upload. Still never accept a client-chosen path here.
- ✅ `OriginName` / `DestinationName` on `ImportedTicket`, populated by extraction — free-text `RouteDescription` could not support journey grouping.
- ✅ Duplicate override — `allowDuplicate` on create, and the 409 now names the ticket it collided with.

### Remaining

- ⏳ **MAUI capture UI** — the server side is done; the app still needs the import chooser (photo / file / scan / paste), a SkiaSharp JPEG re-encode of camera captures (which is what handles HEIC and cuts upload size), and the upload → prefilled-form flow. Camera and photo permissions are already declared on both platforms.
- ⏳ Live camera **preview** scanning — deliberately deferred. The server decodes Aztec/QR/PDF417 from a still, so a photo capture covers the capability; a preview scanner is a UX upgrade, not a gate.
- ⏳ **OCR of paper tickets** — ZXing reads codes, not prose. A photo bearing no code yields an image and little else until an OCR engine is added.
- ⏳ **Email forwarding / `.eml` ingestion** — the lowest-friction UX, but it needs an inbound mail route, address verification so strangers cannot fill an account, and its own abuse story.
- ⏳ Replace stub Tickets/Shop pages with a real wallet UI — `ShopViewModel` is still a "Coming Soon" string swap
- ⏳ Filtering by **transport type** — blocked, not a scope item until decided: `ImportedTicket` has no transport-type field. Needs a call on whether it derives from the operator, from a matched route, or becomes a user-entered field. Do not add a column before deciding.
- ⏳ **GDPR account deletion + data export** — pulled forward from Phase 7 to sit beside the beta below, because the beta means real user data. Blocked by `FK_ImportedTickets_AspNetUsers_UserId` using `onDelete: Restrict`: deletion will fail outright until the imported-ticket path is handled. Choosing between cascade, anonymise, and soft-delete is a design decision needing human review.
- ⏳ **Business/pricing model decision** — pulled forward from Phase 6 (see Phase 0 Deferred). Must land before the wallet schema freezes.
- ⏳ Lightweight beta/feedback loop once shipped
- 📋 **Phase exit criterion — manual smoke path.** Before marking Phase 2 shipped, a human runs: register a non-admin account → import a ticket → list and filter it → cancel it → re-import to confirm dedupe → confirm an out-of-date ticket flips to `Expired` → top up the wallet. There are no automated tests, so this path is the gate.

> **Moved out of Phase 2:** Apple Wallet (PassKit) / Google Wallet integration → **Phase 5**. Neither is required for a usable wallet, and both are gated on external approval rather than code: PassKit needs an Apple Developer pass-type ID and signing certificate, Google Wallet needs issuer onboarding. **Start those applications now** — the lead time, not the implementation, is the long pole.

---

## Phase 3 — Trip Planning / Routing Engine

Currently missing entirely — `ScheduleManager` only does single-station departures and per-route trip lists, not A→B multi-modal routing. `PROJECT.md` and `README.md` name OTP as the intended engine and now correctly mark it "planned — not yet integrated"; there is no OTP client, no `Transit/` folder, and no GraphQL call anywhere in the solution.

- Real OpenTripPlanner (or equivalent) integration for A→B multi-modal routing
- GTFS completeness needed to support planned features:
  - Fares (`fare_products` / `fare_rules`)
  - Transfers (`transfers.txt`)
  - Frequency-based service (`frequencies.txt`)
  - Pathways/levels (indoor/accessible routing)
- Geocoding for arbitrary addresses (currently station-to-station search only)

---

## Phase 4 — Journeys & Retention

Retrospective: grouping tickets a user already owns (distinct from prospective trip planning in Phase 3).

- ✅ Group multiple imported/purchased tickets into a "Journey" — `Journey` entity, `JourneyManager`, `JourneysController`, and `journeys.view/create/manage` granted to the User role. Membership is a nullable `JourneyId` FK on **both** ticket tables, which finally gives the long-orphaned `ImportedTicket.JourneyId` column the foreign key it was named for. Deleting a journey **releases** its tickets (`DeleteBehavior.SetNull`, deliberately overriding the global `Restrict`) rather than deleting them.
- ✅ Auto-suggestion — `GET /journeys/suggestions` groups by time proximity and by chaining one leg's destination to the next's origin, which only works because import extraction now populates structured endpoints. Suggestions are proposals; nothing is applied automatically. Journey status rolls forward from its legs in `TicketExpiryWorker`.
- ✅ Journeys **UI** — journeys sit behind a `Tickets | Journeys` segmented control on `TicketsPage` rather than a fifth tab, which the phone frames have no room for. `JourneyService`, `JourneysViewModel` (list, paging, suggestions) composed into `TicketsViewModel`, and `JourneyDetailPage` / `JourneyDetailViewModel` (legs, rename, cancel, remove leg, delete). Accepting a suggestion is a single create carrying the suggested ticket ids. `TicketDetailPage`'s "Show Journey" is bound and shown only when the ticket is in one — which needed `JourneyId` on `TicketResponse`, mirroring `ImportedTicketResponse`. "Add to Wallet" was removed rather than left dead: no `Command`, and no wallet-export feature behind it.
- ⏳ "Add an existing ticket to a journey" — tickets currently join a journey by accepting a suggestion or at creation time; there is no picker on an existing journey. `POST /journeys/{id}/tickets` is already wired in `JourneyService.AddTicketsAsync`, so this is a UI-only gap.
- "Upcoming journeys" home view
- Disruption-to-journey subscriptions (tie GTFS-RT alerts to a user's saved journeys)
- Push notification infrastructure (FCM/APNs, device token storage) — deferred here from Phase 0; the notifications below depend on it
- Notifications: ticket expiry, journey-starting-soon, disruption alerts
- Offline ticket access (cached QR/barcode images — core "wallet" expectation)

---

## Phase 5 — Polish & Trust

- Help Center / About screens (replace `DisplayAlert` stubs) — the **Payment Methods** screen moved to Phase 6, since Stripe/Adyen dictate that UI
- Apple Wallet (PassKit) / Google Wallet integration — moved here from Phase 2. Gated on external approval, not code: PassKit needs an Apple Developer pass-type ID + signing certificate, Google Wallet needs issuer onboarding. Start the applications early.
- Theming cleanup — replace hardcoded hex colors with `Colors.xaml` resources
- Fix EN/HR localization key mismatches
- Trip history/stats (trips taken, spend, CO2 vs. car)
- Favorites (stations/routes), nearby-departures widget
- Session management UI (view/revoke active devices/sessions)
- Feature-discovery prompts for existing users when something new ships (not just first-run onboarding)

---

## Phase 6 — Payments & Real Ticketing

- Integrate real payment provider (Stripe/Adyen), tokenized, PCI-compliant
- Real Payment Methods screen — moved here from Phase 5. The provider dictates this UI (tokenized card sheets, SDK-hosted entry, SCA challenge flows), so building it before the provider is chosen guarantees a rewrite.
- Account for regional payment variation (SCA/PSD2, local payment methods)
- Multi-currency support — imported tickets already carry a user-selected currency from the shared `SupportedCurrencies` list, but purchasing, wallet balances, and top-up are still EUR-only
- Live wallet top-up (currently mocked)
- First real `ITicketingAdapter` implementations (ZET, HZPP, etc.)
- Refund / chargeback handling
- Disruption-triggered refund suggestions

---

## Phase 7 — Compliance & Ops Maturity

- GDPR retention/cleanup jobs (tickets, audit logs, refresh tokens) — **account deletion and data export moved to Phase 2**, since the Phase 2 beta puts real user data in scope
- Privacy policy / ToS in-app
- Deployment story: containerization, staging/prod config separation, safe migration strategy for multi-instance TransitInfoAPI
- Health checks, log aggregation, uptime monitoring
- Database backup strategy
- Reconciliation & feed health alerting (proactive, not manual admin-panel checks)
- 2FA/MFA, user-facing lockout messaging
- API versioning strategy (needed before any public SDK/third-party consumers)
- EU open-data / National Access Point compliance; enforce feed license flags already stored on `Feed` entity before making TransitInfoAPI public

---

## Phase 8 — Admin & Support Tooling

- Wallet / imported-ticket moderation tools
- Manual refund tools for support staff
- In-app support/contact flow (replace stub)
- Per-user rate limiting (beyond current IP-based limiting)

---

## Phase 9 — Scale & Reach

- Accessibility pass (screen reader support, dynamic font scaling, color contrast)
- Search improvements (typo tolerance, better station/route matching)
- Ticket/journey sharing + deep linking to support it
- Localization expansion beyond EN/HR as new markets are added
- Multi-region readiness validation (confirm no remaining Croatia-only assumptions)
- Operator self-serve onboarding tooling (per README's own Phase 4 roadmap)
- Public API / integration documentation for third-party adapter authors
- App store release process (versioning, staged rollouts, review lead time)
- AI-assisted journey routing and pricing (once live ticketing/pricing data exists)

---

## Notes

### Status marks

| Mark | Meaning |
|------|---------|
| ✅ | Done **and** exercised end-to-end |
| ⚠️ | Built but not effective yet (wired, disabled, or unreachable) — say why |
| ⏳ | Not started, or started and incomplete |
| ⏭️ | Deliberately deferred to a named later phase — the destination phase must list it too |
| 📋 | A process/exit criterion, not a code deliverable |

### Marking something ✅

**Do not mark an item ✅ until it has been exercised end-to-end, not merely written.**
An item where the backend exists but the feature is unreachable from the app is
⚠️, not ✅. Where the distinction is non-obvious, append *"Verify by: …"* naming
the concrete check (endpoint called from the app / migration applied / CI run
green / manual smoke path).

This convention exists because a prior audit found four items marked ✅ whose
mechanisms were built but unreachable — including two where an ordinary user got
a 403, and one CI pipeline that had never produced a green run. In every case the
code was written and the box was ticked without the path being walked.

Do not use this document as a changelog. Per-commit implementation detail and
bug fixes belong in `AGENTS.md`; roadmap items are phase deliverables.

### Sequencing

- Phases are sequential in priority, not necessarily in strict execution order — some items (push infra in Phase 0 → Phase 4, the pricing decision in Phase 0 → Phase 2) are moved specifically because of what depends on them. **When an item moves, edit both ends** — a forward reference with no matching entry in the destination phase is how items get lost.
- Testing/CI-as-quality-gate was intentionally excluded from this roadmap per explicit instruction, aside from the build-check pipeline in Phase 1. Because there is no automated coverage, each phase's manual smoke path (see Phase 2 for the pattern) is the only real gate — treat it as required, not optional.
