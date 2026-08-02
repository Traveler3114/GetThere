# GetThere Documentation

## Start here

**[reference/overview.md](reference/overview.md)** — what the system does, why it is five projects,
how they connect, and where the genuinely hard problems are.

---

## Reference documentation

Full technical reference for all five projects. Written to explain *why* things are built the way
they are, not just what each type does.

### Shared contracts

- **[shared/contracts.md](reference/shared/contracts.md)** — every DTO and enum crossing the wire,
  with wire values and validation rules

### GetThereAPI

- **[architecture.md](reference/getthere-api/architecture.md)** — layering, the permission model,
  refresh tokens, rate limiting, static hosting, seeding, configuration
- **[endpoints.md](reference/getthere-api/endpoints.md)** — every route, its policy, its error codes
- **[domain-logic.md](reference/getthere-api/domain-logic.md)** — the money path, deduplication,
  journeys, the background worker, the ticketing SDK
- **[ticket-import.md](reference/getthere-api/ticket-import.md)** — upload, byte sniffing, the four
  extractors, storage and path containment
- **[transit-integration.md](reference/getthere-api/transit-integration.md)** — **historical.** The
  upstream client, the service-account hop and the map proxy were all removed; GetThereAPI makes no
  call to TransitInfoAPI. Kept for the allowlist and 502-not-500 reasoning, which any future
  integration has to answer. See also [map-proxy-migration.md](map-proxy-migration.md)

### TransitInfoAPI

- **[architecture.md](reference/transitinfo-api/architecture.md)** — layering, service lifetimes,
  auth, crash recovery, SSRF protection, configuration
- **[feed-pipeline.md](reference/transitinfo-api/feed-pipeline.md)** — GTFS import, immutable
  versioning, polling, auto-deactivation
- **[reconciliation.md](reference/transitinfo-api/reconciliation.md)** — station identity, OnestopIds,
  matching, merge and unmerge
- **[realtime.md](reference/transitinfo-api/realtime.md)** — GTFS-RT, GBFS, departures, the in-memory
  caches
- **[endpoints.md](reference/transitinfo-api/endpoints.md)** — every route and permission

### GetThere (MAUI client)

- **[architecture.md](reference/getthere-client/architecture.md)** — DI, the HTTP stack, navigation,
  localization, the map WebView
- **[ticket-import.md](reference/getthere-client/ticket-import.md)** — capture, image normalisation,
  view models, UI converters

### Databases

- **[db/getthere-schema.md](reference/db/getthere-schema.md)** — tables, indexes, migration history
- **[db/transitinfo-schema.md](reference/db/transitinfo-schema.md)** — tables, indexes, spatial columns

---

## Operational notes

- [money-path-defects.md](money-path-defects.md) — known issues in the money path
- [secrets-rotation.md](secrets-rotation.md) — rotating credentials
- [database-drift.md](database-drift.md) — schema drift
- [transitinfodb-rebaseline.md](transitinfodb-rebaseline.md) — why TransitInfoDb has one migration
- [map-proxy-migration.md](map-proxy-migration.md) — how the map moved behind the proxy
- [guides/ef-database-commands.md](guides/ef-database-commands.md) — EF Core commands

## Architecture notes

- [architecture/integration-guide.md](architecture/integration-guide.md)
- [architecture/map-features.md](architecture/map-features.md)

## Project-level

- [../PROJECT.md](../PROJECT.md) — product intent and scope
- [../ROADMAP.md](../ROADMAP.md) — what is planned
- [../AGENTS.md](../AGENTS.md) — conventions and rules for working in this repo
- [../VERIFY.md](../VERIFY.md) — **temporary.** What to check on the current feature branch, none of which
  has been compiled. Delete it once that branch is merged
- [changelog.md](changelog.md) — per-session implementation detail
