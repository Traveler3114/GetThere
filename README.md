# GetThere

**Google Maps + Booking.com — for public transportation.** All of it. Trams, buses, trains, ferries, flights, bike share, scooter share, cable cars, coaches. Everything public. No private cars, no taxis.

One account. Never need another operator app again. Plan journeys, book tickets, all in one place. AI routing later — cheapest, fastest, balanced.

---

## Architecture — Two Platforms

GetThere is split into two independent platforms connected by a one-way dependency:

### TransitInfoAPI — The Map Platform
Completely standalone. Own database, own identity. Could be made public one day.

- Operator identity & GlobalId generation
- GTFS feed management, importing & enrichment
- Canonical stations (one real place = one record)
- Reconciliation of shared stations across feeds
- Routes & schedules via OTP
- Real-time vehicle positions
- Mobility stations (bikes, scooters)
- Clean reconciled transit data via its own REST API

**Knows nothing about GetThereAPI.** No users, no wallets, no tickets. Pure transit data.

### GetThereAPI — The Business Platform
Private always. The competitive moat.

- User accounts & identity
- Wallets & payments
- Ticketing adapter system
- Operator commercial relationships
- Ticket purchasing & storage

References TransitInfoAPI operators by GlobalId. **Cannot add ticketing for an operator that doesn't exist in TransitInfoAPI.** One-way dependency only.

### The SDK
Built for internal use first, public later.

- Standard interface every ticketing adapter must implement
- How operators connect their backend to GetThereAPI
- Internally you write the adapters; later operators write their own
- Enforces that an operator must exist in TransitInfoAPI before ticketing can be added

### Map → Ticketing Connection
One-way flow only: TransitInfoAPI generates a GlobalId → GetThereAPI references it. Never the other way around.

User taps a station on the map:
1. TransitInfoAPI knows which operators serve it
2. GetThereAPI checks if any of those operators have ticketing
3. If yes → show buy button
4. If no → show information only

### Two Databases

| Database | Owner | Contents |
|----------|-------|----------|
| **TransitDB** | TransitInfoAPI | Operator identity, canonical stations, feeds, reconciliation, mobility data |
| **AppDB** | GetThereAPI | Users, accounts, wallets, payments, ticketing config. References TransitInfoAPI GlobalIds. |

---

## Phase 1 Scope

**Croatia focused, but not Croatia only.** Any operator that touches Croatian soil is included:

- ÖBB (Zagreb–Vienna) is included
- FlixBus routes through Croatia are included
- Croatian infrastructure is modelled fully
- Outside Croatia, only endpoints matter

Operators in scope: **ZET**, **HZPP**, **Nextbike**, and any operator whose services cross Croatian borders.

---

## Map

TransitLand inspired, two layers:

### Layer 1 — Canonical
One record per real physical place. **Zagreb Glavni Kolodvor** exists once, regardless of how many operators serve it. This is what the map shows.

### Layer 2 — Feed Data
Raw imported GTFS data. HZPP's stop ID and ÖBB's stop ID both point to the same canonical station.

### Reconciliation System
Confidence scoring on import:
- Auto-merge when geographically close **and** name similar
- Flag for manual review when uncertain
- **Never block import** waiting for review

### Map Filters
All frontend, no database impact:
- Show/hide route lines
- Filter by transport type
- Show/hide vehicles
- Show/hide bike stations

Route lines for long-distance are hidden by default but user can toggle. Shape data is stored when available, drawn or not based on filter state.

---

## Operator Model

Three concerns, completely separated:

| Concern | Purpose |
|---------|---------|
| **Core identity** | Who the operator is. Name, country, type. The bridge between everything. |
| **Transit feed config** | GTFS URLs, realtime feeds, feed IDs. What the map uses. Can exist without ticketing. |
| **Ticketing config** | Adapter type, credentials, JSON config. Can exist without map data. |

Both ticketing and map data are linked through the core operator identity.

### Two States Per Operator

| State | Requirement |
|-------|-------------|
| **Map / routing** | Always works if GTFS exists |
| **Ticketing** | Only works when operator has given API access |

---

## Ticketing

**You are the frontend.** The operator's backend stays completely untouched. GetThere talks to their backend exactly like their own app does.

### Adapter System

Every operator has an adapter implementing the same interface:

| Method | Purpose |
|--------|---------|
| `GetOptions()` | What tickets does this operator sell |
| `GetInputs()` | What extra info do you need from the user |
| `Purchase(inputs)` | Here is payment and info, give me a ticket |
| `GetStatus()` | Check ticket validity |

### Adapter Examples

| Operator | Adapter Behavior |
|----------|-----------------|
| **ZET** | No extra inputs. Returns QR code. Time-activated on vehicle scan. |
| **HZPP** | Needs origin, destination, date. Returns QR code tied to specific journey. |
| **Nextbike** | Needs duration. Returns unlock credential. |

### Vendor-Based Integrations

One adapter powers many operators. Example: **Masabi** — one adapter, dozens of operators.

### Map-to-Ticketing Connection

User taps a stop → stop belongs to feed ID → feed ID maps to operator → operator has ticketing config → show buy button. Station context is passed to the Shop page for pre-filling (e.g., HZPP origin station).

---

## Database Design Principles

- **Multi-region ready** from day one. No refactoring later.
- TransitLand inspired, but with a ticketing layer on top.
- **Reconciliation system** built in from the start — one pin per real place on the map.
- Shape data stored when available, **optional**.
- Long-distance routes: stations only modelled in detail. No need for full route shape.

---

## Future Phases

| Phase | Scope |
|-------|-------|
| **Phase 1** | Croatia focus. TransitInfoAPI setup with ZET, HZPP, Nextbike, any operator touching Croatia. GetThereAPI with accounts, wallets, ticketing for available operators. Internal SDK — you write all adapters yourself. |
| **Phase 2** | More operators, more countries. Expand TransitInfoAPI feeds. Add more ticketing adapters. SDK still internal. |
| **Phase 3** | Open TransitInfoAPI. Make the map platform public. Other developers can build on top. Ticketing stays private in GetThereAPI. |
| **Phase 4** | Public SDK. Operators onboard themselves — write their own adapters via self-serve portal. |
| **Phase 5** | AI routing. Cheapest, fastest, balanced. Cross-operator journey planning. Purchase the entire journey in one flow. |
