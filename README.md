# GetThere

**Google Maps + Booking.com — for public transportation.** All of it. Trams, buses, trains, ferries, flights, bike share, scooter share, cable cars, coaches. Everything public. No private cars, no taxis.

One account. Never need another operator app again. Plan journeys, book tickets, all in one place. AI routing later — cheapest, fastest, balanced.

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
| **Phase 1** | Croatia. ZET, HZPP, Nextbike, any operator touching Croatia. Prove the concept. |
| **Phase 2** | More operators, more transport types, expand coverage. |
| **Phase 3** | Multi-country expansion, flights, ferries. |
| **Phase 4** | AI routing. Cheapest, fastest, balanced. Learns user preferences over time. |

---

## What We Have NOT Designed Yet

These are explicitly out of scope for the current design phase:

- The actual database schema
- The adapter interface in code
- The reconciliation algorithm
- The SDK structure
- The admin review interface
