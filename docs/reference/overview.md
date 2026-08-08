# GetThere — System Overview

> Start here. This document explains what the system does, why it is split into five projects, and
> how they fit together. Every other reference document assumes what is written here.

---

## What the product is

A travel wallet. One app that holds **every** ticket a traveller has — the ones it sold them and the
ones they already had — and shows them the transit network those tickets run on.

That "and the ones they already had" is the whole premise. An app that only holds tickets it sold is
a vending machine; people already have those, one per operator, and none of them is where the rest of
your tickets live. Being the *only* wallet a traveller opens means accepting a PDF from a rail
operator, an Apple Wallet pass from an airline, a photo of a paper ticket, and a calendar invite from
a booking confirmation — and making them all look like tickets.

Two capabilities follow, and they account for most of the code:

1. **Import** — read a ticket out of a file the user already has, without inventing data.
2. **Transit data** — know what "Zagreb Glavni kolodvor" is, so a ticket means something and a map
   can show where you are going.

Both turn out to be much harder than selling a ticket, which is why the interesting parts of this
codebase are the [import pipeline](getthere-api/ticket-import.md) and
[station reconciliation](transitinfo-api/reconciliation.md).

---

## The five projects

```
┌──────────────────┐    HTTPS + user JWT     ┌──────────────────┐  HTTPS + service JWT  ┌──────────────────┐
│    GetThere      │ ──────────────────────► │   GetThereAPI    │ ────────────────────► │  TransitInfoAPI  │
│   .NET MAUI app  │ ◄────────────────────── │  ASP.NET Core    │ ◄──────────────────── │  ASP.NET Core    │
│  Android/iOS/    │                         │                  │                       │                  │
│  macOS/Windows   │                         │  users, wallets, │                       │  stations, routes│
│                  │                         │  tickets,        │                       │  schedules,      │
│                  │                         │  journeys, import│                       │  live vehicles   │
└────────┬─────────┘                         └────────┬─────────┘                       └────────┬─────────┘
         │                                            │                                          │
         │        ┌──────────────────┐                │ EF Core                                  │ EF Core
         └───────►│  GetThereShared  │◄───────────────┘                                          │
                  │  DTOs and enums  │                                                           │
                  └──────────────────┘                                                           │
                                              ┌──────────────┐                          ┌────────────────┐
                                              │  GetThereDb  │                          │ TransitInfoDb  │
                                              └──────────────┘                          └────────────────┘
```

| Project | Files | What it owns |
|---|---|---|
| **[GetThere](getthere-client/architecture.md)** | 64 | The app. Pages, view models, device integration, navigation |
| **[GetThereAPI](getthere-api/architecture.md)** | 83 | Users, wallets, tickets, journeys, the import pipeline |
| **[GetThereShared](shared/contracts.md)** | 31 | The DTOs and enums crossing the wire, plus `Extraction/` — reading a ticket out of a file. Extraction is shared by the API and the client so both read it identically, and so a device can import with no account and no signal |
| **[TransitInfoAPI](transitinfo-api/architecture.md)** | 108 | GTFS feeds, station identity, schedules, realtime, mobility |

Plus `tests/GetThere.Tests` — one xUnit project covering all of them, with `InternalsVisibleTo` from
both APIs.

### Why four and not one

**The client is separate** because it has to be — it ships to app stores.

**GetThereShared exists** so a DTO change breaks compilation on both sides at once rather than failing
silently over the wire at runtime. It contains almost no behaviour: contracts, enums, and a handful of
helpers (`MoneyFormatter`, `OperationResult`, `PagedResult`) that both sides genuinely need to agree
on.

**The two APIs are separate** because they are different systems wearing similar clothes:

| | GetThereAPI | TransitInfoAPI |
|---|---|---|
| Data source | User actions | Operator-published feeds |
| Write pattern | Small transactional writes | Bulk imports rewriting whole tables |
| Data scope | One user's data | Public, shared by everyone |
| Sensitivity | Personal and financial | Public reference data |
| Changes when | A user does something | An operator republishes |

Merging them would put a feed import that rewrites millions of rows in the same database as the wallet
ledger, and would make every transit schema change a risk to the money path.

**TransitInfoAPI references no other project in this solution.** Not `GetThereShared`, not
`GetThereAPI`, not the client — this is a hard boundary, and the `.csproj` carries a comment where
the reference would otherwise go. Each service has its own `PermissionKeys`, `AppException`,
`RoleNames`, `AuditLog`, `RefreshToken` and `GeoConstants`; that duplication is the intended cost.

The reason is that the services only *look* similar. They have separate user stores, separate
permission vocabularies (feeds and stations versus wallets and tickets) and separate roles
(`Admin`/`Client` versus `Admin`/`User`). Types that match today match by coincidence, not by
contract — so sharing them would couple two independent release cycles and let a change made for one
service silently alter the other. The only channel between them is HTTP.

`GetThereShared` is therefore shared by exactly two projects: the client and GetThereAPI.

---

## The one-way rule

```
client → GetThereAPI                        (all business data)
client → TransitInfoAPI                     (the map, and only the map)
```

**The two services do not call each other.** GetThereAPI once brokered transit data for the client,
for three reasons:

1. **Credentials.** Reaching TransitInfoAPI's *authorized* surface needed a service-account login.
   Direct client access would put that credential in an app binary, where anyone can read it.
2. **Authorization.** TransitInfoAPI has admin endpoints — feed imports, station merges,
   reconciliation. The service account could reach them; a client holding it could too.
3. **Coupling.** TransitInfoAPI's contracts could change without an app release, because only
   `TransitInfoApiClient` was pinned to them.

**The map is what made all three moot.** It is a WebView pointed at a page served *by
TransitInfoAPI*, reading that service's `[AllowAnonymous]` endpoints same-origin — no proxy, no
service account, no bearer token, no CORS. It carries no credential, touches no admin endpoint, and
renders GeoJSON that GetThereAPI was only re-shipping verbatim anyway. Once that was true, the
brokering had one caller left — an admin status dot — and on 2026-08-02 it was removed entirely,
along with `TransitInfoApiClient`, `MapManager`, `MapProxyController` and the `map.view` permission.

**For everything it does with a user's data, the client still talks only to GetThereAPI.** That rule
is unchanged; what changed is that GetThereAPI no longer talks to anything else on its behalf. It replaced a proxy that existed purely to bridge two origins; see
[`docs/map-proxy-migration.md`](../map-proxy-migration.md) and
[the map section](getthere-client/architecture.md#the-map-a-webview-and-nothing-else).

The practical consequence: those endpoints are a **public surface**. Treat a change to `stations`,
`routes`, `mobility/stations`, `stations/{id}/departures`, `realtime/vehicles` or `stations/search`
as a change anyone can see.

The single link between the two domains is a **string, not a foreign key**:
`TicketingAdapter.TransitInfoGlobalId` points at an operator's Onestop ID upstream. Looseness is the
point — the two databases must move independently.

---

## Tech stack

Everything is **.NET 10**, with central package management (`Directory.Packages.props`) so a security
bump applies everywhere at once. `Directory.Build.props` turns on .NET analyzers at
`latest-recommended` with `EnforceCodeStyleInBuild`; **CI builds with `-warnaserror`**, so anything the
analyzers surface must be fixed or explicitly suppressed.

| Area | Choice | Why |
|---|---|---|
| APIs | ASP.NET Core 10, EF Core 10, SQL Server | |
| Auth | JWT + rotating refresh tokens, ASP.NET Identity | |
| Client | .NET MAUI 10, CommunityToolkit.Mvvm | One codebase, four platforms |
| Spatial | NetTopologySuite | Real geometry for stations, routes, hulls |
| GTFS | CsvHelper, Google.Protobuf | CSV static feeds; protobuf realtime |
| Import | PdfPig, ZXing.Net, Ical.Net, SkiaSharp | PDF text, barcodes, calendar invites, image decode |
| Tests | xUnit, coverlet | |
| Crash reporting | Sentry.Maui | Client only |

Two pinning decisions worth knowing: `Microsoft.OpenApi` is pinned because it transitively resolves to
a version with a known high-severity advisory (NU1903), and `SkiaSharp` is pinned to 4.148.0 to match
what the MAUI packages already resolve — transitive pinning is on, so a newer version would bump the
app too.

---

## How the pieces actually connect

### Authentication, end to end

```
1. App           POST /auth/login                          → access JWT + refresh token
2. App           stores both in SecureStorage
3. App           sends "Authorization: Bearer <jwt>" on every call
4. GetThereAPI   validates the JWT, then DynamicClaimsTransformation
                 STRIPS its role/permission claims and reloads them from the database
5. Policy check  Admin role, or a "permission" claim matching the endpoint's key
```

Step 4 is the non-obvious one. Permissions are **not** trusted from the token, because a token lives
up to an hour and revocation has to take effect sooner. Claims are reloaded per request, cached 30 s
sliding with a **5-minute absolute ceiling** — the ceiling is the real control, since sliding alone
never lapses for an active user.

> **Removed, 2026-08-02.** GetThereAPI used to authenticate to TransitInfoAPI the same way, as the
> `getthere-api` service account holding the `Client` role, caching its token in `static` fields with
> double-checked locking and retrying once on 401. It no longer calls TransitInfoAPI at all, so
> `TransitInfoApi:ClientSecret` is gone from its configuration.
>
> This also retires what used to be the most common integration failure between the two services:
> `Seed:ServiceAccountPassword` in TransitInfoAPI and `TransitInfoApi:ClientSecret` in GetThereAPI
> were two halves of one credential configured in two places, with nothing validating that they
> matched, and a mismatch surfaced as a 502 on every map endpoint. The `getthere-api` account still
> exists upstream and is now dormant.

### Buying a ticket

```
App  →  GET  /tickets/options
App  →  POST /tickets/purchase   { adapterId, optionId }   + Idempotency-Key header
```

Server-side, in three stages that exist so money and ticket cannot diverge:

1. Validate everything that can fail without touching money — including that an SDK implementation is
   registered, and that wallet and option currencies match.
2. Debit, write the ledger row and a `Pending` purchase, **commit and close the transaction**.
3. Call the operator adapter with nothing open. On any failure, write a compensating refund.

No transaction and no wallet lock is ever held across an outbound HTTP call. The cost is that a crash
between stages 2 and 3 leaves a `Pending` purchase — recoverable, surfaced on the admin overview, with
no automatic sweep. Full detail in
[domain-logic.md](getthere-api/domain-logic.md#the-purchase-path).

The `Idempotency-Key` is not optional in practice: the client's `AuthenticatedHttpHandler` **replays**
requests after a 401 token refresh, and without a key that replay is a second charge.

### Importing a ticket

```
App  →  capture (camera / library / file / paste)
App  →  re-encode images to upright JPEG  ← makes iOS HEIC work at all
App  →  POST /importedtickets/upload      → { blobKey, extraction }
        ── user reviews and corrects the draft ──
App  →  POST /importedtickets             → ticket created
```

Deliberately two steps. What a file yields ranges from near-complete (a wallet pass is structured
data) to nothing at all (a photo with no barcode), so **a human confirms before a ticket exists**.
`extraction.detectedFields` tells the UI which values were read off the file versus guessed.

The blob key is server-minted, single-use, and resolved against the caller's own unconsumed uploads —
which is what makes it safe to let a client name a stored file at all.

### Showing the map

```
App  →  WebView → TransitInfoAPI /map/public.html?lang=…
Page →  TransitInfoAPI /stations, /routes, /mobility/stations,
        /realtime/vehicles, /stations/search, /stations/{id}/departures
        same-origin, anonymous — no proxy, no token, no CORS
```

The map is the sole place the client reads TransitInfoAPI directly; everything else goes through
GetThereAPI. It used to be proxied — `docs/map-proxy-migration.md` explains that arrangement and why
it went away.

---

## Cross-cutting patterns

The same ideas recur in both APIs. Recognising them makes each service easier to read.

| Pattern | Where |
|---|---|
| Permission claims, not roles; Admin bypasses all | Both APIs |
| `AppException(message, status, code)` → RFC 9457 problem JSON | Both APIs |
| **Error *codes* are the contract**, messages are a fallback | Both APIs → `ApiMessageMapper` |
| Enums stored as strings in the database | Both databases |
| `DeleteBehavior.Restrict` globally, overridden deliberately | Both databases |
| Filtered unique indexes for conditional constraints | Both databases |
| Auth rate limiter at 10/min per IP | Both APIs |
| `/admin` static console with **no** auth gate, hardened by CSP | Both APIs |
| Refuse to seed an admin password outside Development | Both APIs |
| Bound any archive from outside before decompressing | pkpass extractor, GTFS importer |
| Path containment checked as a **string** before use as a path | Ticket file store, feed storage |
| Never trust a declared content type — sniff the bytes | Upload pipeline |

That last group is worth stating as a principle, because it shows up in four places independently:
**anything a caller supplies — filename, content type, archive metadata, identifier — is a hint, never
a gate.**

---

## Where the interesting problems are

If you are trying to understand this system rather than navigate it, read these three in order:

1. **[Station reconciliation](transitinfo-api/reconciliation.md)** — four operators call Zagreb main
   station four different things and nothing in GTFS says they are related. The raw/canonical split,
   deterministic OnestopIds, route-set and direction matching, and why thresholds are snapshotted onto
   every decision.
2. **[The ticket import pipeline](getthere-api/ticket-import.md)** — extracting a ticket from an
   arbitrary file without inventing data, and the confirm-before-create flow that follows from it.
3. **[The purchase path](getthere-api/domain-logic.md#the-purchase-path)** — moving money against an
   unreliable external service without holding a lock across it.

---

## Running it

```bash
dotnet build GetThere.slnx
```

```bash
dotnet test tests/GetThere.Tests/GetThere.Tests.csproj
```

Both APIs refuse to start without a valid `Jwt:Key` (≥32 bytes, not `CHANGE-ME`) and a connection
string. Use user secrets:

```bash
dotnet user-secrets set "Jwt:Key" "<64-char-key>" --project GetThereAPI/GetThereAPI.csproj
```

**Migrations differ between the two services.** TransitInfoAPI applies them automatically at startup;
GetThereAPI does not — run them explicitly:

```bash
dotnet ef database update --project GetThereAPI/GetThereAPI.csproj
```

**Start order no longer matters.** It used to: TransitInfoAPI had to come up first because it creates
the `getthere-api` service account GetThereAPI authenticated with. With that call path removed, the
two APIs are independent at startup and can be brought up in any order, or one without the other. The
app needs GetThereAPI for business data and TransitInfoAPI for the map, and degrades on whichever is
missing rather than failing to start.

The app's API base URL is **compile-time** (`Helpers/ApiEndpoints.cs`), with `10.0.2.2` for the Android
emulator. A released build cannot be repointed — a known open item.

---

## Test coverage

`tests/GetThere.Tests` targets the parts where being wrong is expensive:

| Test | Guards |
|---|---|
| `Money/PurchaseFlowTests` | The three-stage purchase, refunds, idempotency |
| `Auth/RefreshTokenTests` | Rotation, reuse detection |
| `AuthorizationMatrixTests` | Endpoint-to-permission mapping |
| `FeedUrlSsrfTests` | Feed URL SSRF protection |
| `ImportedTickets/TicketFileSnifferTests` | Magic-number detection |
| `ImportedTickets/TicketFileStoreTests` | Path traversal |
| `ImportedTickets/TicketExtractionTests` | The extractors |
| `Journeys/JourneyManagerTests` | Grouping, suggestions, status roll-up |
| `LevenshteinDistanceTests`, `ReconciliationGridTests` | Matching correctness |
| `PagedResultContractTests` | Pagination shape — the `data`/`Items` bug |
| `Money/SupportedCurrenciesTests` | Currency validation |

The pattern: **security boundaries and money, not UI**. Every one of these corresponds to a defect
that was found or a boundary that would be silent if it broke.

---

## Document map

### Reference

| Document | Covers |
|---|---|
| [shared/contracts.md](shared/contracts.md) | Every DTO, every enum with wire values |
| [getthere-api/architecture.md](getthere-api/architecture.md) | Layering, auth, permissions, rate limiting, seeding, config |
| [getthere-api/endpoints.md](getthere-api/endpoints.md) | Every route, its policy, its error codes |
| [getthere-api/domain-logic.md](getthere-api/domain-logic.md) | Money path, dedupe, journeys, background worker |
| [getthere-api/ticket-import.md](getthere-api/ticket-import.md) | Upload, sniffing, extractors, storage |
| [getthere-api/transit-integration.md](getthere-api/transit-integration.md) | The client, caching, the allowlist |
| [transitinfo-api/architecture.md](transitinfo-api/architecture.md) | Layering, lifetimes, auth, crash recovery, config |
| [transitinfo-api/feed-pipeline.md](transitinfo-api/feed-pipeline.md) | GTFS import, versioning, polling |
| [transitinfo-api/reconciliation.md](transitinfo-api/reconciliation.md) | Station identity, OnestopIds, merge/unmerge |
| [transitinfo-api/realtime.md](transitinfo-api/realtime.md) | GTFS-RT, GBFS, departures, in-memory caches |
| [transitinfo-api/endpoints.md](transitinfo-api/endpoints.md) | Every route and permission |
| [getthere-client/architecture.md](getthere-client/architecture.md) | DI, HTTP stack, navigation, localization, map |
| [getthere-client/ticket-import.md](getthere-client/ticket-import.md) | Capture, image normalisation, view models, converters |
| [db/getthere-schema.md](db/getthere-schema.md) | Tables, indexes, migrations |
| [db/transitinfo-schema.md](db/transitinfo-schema.md) | Tables, indexes, spatial columns |

### Existing project documents

| Document | Covers |
|---|---|
| [../../PROJECT.md](../../PROJECT.md) | Product intent and scope |
| [../../ROADMAP.md](../../ROADMAP.md) | What is planned |
| [../../AGENTS.md](../../AGENTS.md) | Conventions and rules for working in this repo |
| [../changelog.md](../changelog.md) | Per-session implementation detail |
| [../money-path-defects.md](../money-path-defects.md) | Known money-path issues |
| [../secrets-rotation.md](../secrets-rotation.md) | Credential rotation |
| [../database-drift.md](../database-drift.md) | Schema drift notes |
| [../transitinfodb-rebaseline.md](../transitinfodb-rebaseline.md) | Why TransitInfoDb has one migration |
| [../map-proxy-migration.md](../map-proxy-migration.md) | How the map moved behind the proxy |
| [../architecture/integration-guide.md](../architecture/integration-guide.md) | Integration notes |
| [../architecture/map-features.md](../architecture/map-features.md) | Map functionality |
| [../guides/ef-database-commands.md](../guides/ef-database-commands.md) | EF Core commands |

---

## Known gaps, consolidated

Collected here because they are easy to mistake for oversights when reading the code.

| Gap | Consequence |
|---|---|
| `AdapterRegistry` is empty — nothing calls `Register` | **Every purchase returns 503.** The catalogue and purchase path are complete; no operator integration is written |
| No payment provider | `/wallet/topup` mints balance, so it is admin-gated via `wallets.topup` |
| `NoOpTicketFileScanner` | Uploaded files stored and served back unscanned |
| No OCR | A photo with no barcode yields nothing |
| No "add existing ticket to a journey" picker | Tickets join via a suggestion or at creation only |
| Client API URL is compile-time | A released build cannot be repointed |
| `ApiMessageMapper` covers auth codes only | Money-path errors display in English |
| `AnalyticsService` is a stub | Call sites exist; events go nowhere |
| Realtime caches are in-process | **Both services assume a single instance** |
| One configured timezone for all feeds | Wrong for feeds outside `Europe/Zagreb`; `Agency.Timezone` is imported but unused |
| Country detection is bounding boxes | Crude near borders; order-dependent |
| No `Pending` purchase reconciliation sweep | Surfaced on the admin overview, resolved by hand |
| Feed licence fields recorded, not enforced | A compliance record to consult, not a control |
| No account-deletion path | Global `Restrict` blocks it; would need deliberate design |
