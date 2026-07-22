# GetThere — Master Roadmap

This document consolidates the full high-level plan: foundational fixes, existing technical debt, and new features (wallet, journeys, trip planning, etc.), sequenced so that structural work happens before feature work, and feature work happens before monetization.

**Sequencing logic:** fix the shaky foundation first → clean house → ship the flagship value feature before asking for money → build real trip planning → polish what's stubbed → introduce payments only once the base is trustworthy → mature compliance/ops as usage grows → give humans tools to run it → widen reach and add intelligence last.

---

## Phase 0 — Foundation ✅

Blocks everything else. Do not build new user-data features on top of an auth system about to be restructured.

### Completed

- ✅ Unify Identity user-key type across TransitInfoAPI (`int`) and GetThereAPI (`string`) → standardized on `string`
- ✅ Move secrets (JWT keys, admin/service passwords) out of `appsettings.json` into env vars / user-secrets
- ✅ Define a secrets rotation policy → `docs/secrets-rotation.md`
- ✅ Fix password policy mismatch → removed client-side validation; server enforces `RequiredLength = 12` via Identity config + `[MinLength(12)]` on shared `RegisterRequest` DTO
- ✅ Fix `RealtimeManager` race condition → removed dead `UpdateTripUpdate` method
- ✅ iOS Privacy Manifest fix (`NSPrivacyAccessedAPICategoryUserDefaults`) — unblocks future App Store submission
- ✅ Decide MVVM → adopted CommunityToolkit.Mvvm, all 7 pages converted

### Deferred

- ⏭️ Push notification infrastructure (FCM/APNs, device token storage) → moved to Phase 4
- ⏭️ Decide business/pricing model (commission per ticket, subscription, freemium wallet features) → moved to Phase 6

---

## Phase 1 — Cleanup & Baseline Ops ✅

- ✅ Remove dead code — deleted `BreathingBackground`, `AnimatedGradientBehavior`, `SqlHelper`, 4 unused converters (`StatusToColor`, `ProviderIcon`, `InstallBtnText`, `InstallBtnColor`)
- ✅ Deduplicate `RoleDto` / `UserDto` — moved to `GetThereShared.Contracts`, removed 5 local copies across both APIs
- ✅ CI build-check pipeline — `.github/workflows/build-check.yml` compiles both APIs + runs `dotnet format --verify-no-changes`
- ✅ Crash reporting — `Sentry.Maui` added, configured via `Resources/Raw/appsettings.json` DSN
- ✅ Basic analytics — `IAnalyticsService` / `AnalyticsService` stub wired into screen tracking (Shell navigation), login, registration, top-up events
- ✅ Surface GTFS-RT data — added `OccupancyStatus`, `OccupancyPercentage`, `CongestionLevel`, `Speed`, `WheelchairAccessible` to `VehicleResponse`; new `GET /realtime/tripupdates` endpoint

---

## Phase 2 — Wallet Core (flagship pre-purchase feature)

Builds value before ticket purchasing is live.

- Image/document storage strategy (blob storage, size limits, upload scanning)
- Import tickets via: manual entry, photo/PDF upload, QR/barcode scan
- New `ImportedTicket` entity, separate from adapter-purchased `Ticket`
- Filtering/sorting by operator, date, status, transport type
- Replace stub Tickets/Shop pages with a real wallet UI
- Apple Wallet (PassKit) / Google Wallet API integration for lock-screen/quick access
- Duplicate-ticket detection
- Verified vs. unverified ticket marking
- Lightweight beta/feedback loop once shipped

---

## Phase 3 — Trip Planning / Routing Engine

Currently missing entirely — `ScheduleManager` only does single-station departures and per-route trip lists, not A→B multi-modal routing, despite OTP being named in `PROJECT.md`/`README.md` as the intended engine.

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

- Group multiple imported/purchased tickets into a "Journey" (manual + auto-suggested by time/location proximity)
- "Upcoming journeys" home view
- Disruption-to-journey subscriptions (tie GTFS-RT alerts to a user's saved journeys)
- Notifications: ticket expiry, journey-starting-soon, disruption alerts
- Offline ticket access (cached QR/barcode images — core "wallet" expectation)

---

## Phase 5 — Polish & Trust

- Real Payment Methods / Help Center / About screens (replace `DisplayAlert` stubs)
- Theming cleanup — replace hardcoded hex colors with `Colors.xaml` resources
- Fix EN/HR localization key mismatches
- Trip history/stats (trips taken, spend, CO2 vs. car)
- Favorites (stations/routes), nearby-departures widget
- Session management UI (view/revoke active devices/sessions)
- Feature-discovery prompts for existing users when something new ships (not just first-run onboarding)

---

## Phase 6 — Payments & Real Ticketing

- Integrate real payment provider (Stripe/Adyen), tokenized, PCI-compliant
- Account for regional payment variation (SCA/PSD2, local payment methods)
- Multi-currency support (currently hardcoded `"EUR"` everywhere)
- Live wallet top-up (currently mocked)
- First real `ITicketingAdapter` implementations (ZET, HZPP, etc.)
- Refund / chargeback handling
- Disruption-triggered refund suggestions

---

## Phase 7 — Compliance & Ops Maturity

- GDPR: account deletion, data export, retention/cleanup jobs (tickets, audit logs, refresh tokens)
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

- Testing/CI-as-quality-gate was intentionally excluded from this roadmap per explicit instruction, aside from the minimal build-check pipeline in Phase 1.
- Phases are sequential in priority, not necessarily in strict execution order — some items (e.g. push infra in Phase 0, analytics in Phase 1) are pulled early specifically because later phases depend on them.
