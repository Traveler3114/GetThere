# GetThere


# Modular Public Transport & Travel App

## Project Overview

The vision is to create the **ultimate "one-stop" app for all urban, regional, and long-distance travel**:  
- Buy, store, and manage tickets for public transport (trams, buses, trains, city bikes, e-scooters),  
- private operators (intercity buses, commercial rail, even flights),  
- access scheduling, journey planning, and maps—**all with a single account and wallet**.

No more juggling dozens of apps: Think of how streaming was once unified—now, your app aims to unify transport, travel, and multimobility.

---

## Core Directions

## Transitland Configuration

- Set your Transitland API key in:
  - `GetThereAPI/appsettings.Development.json` (local development), or
  - `GetThereAPI/appsettings.json` (shared/default config).
- Do not set `Transitland:ApiKey` to an empty string in higher-priority config sources (e.g. `appsettings.Development.json`, environment variable `TRANSITLAND__APIKEY`, or user-secrets), because an empty value can override a valid lower-priority key.
- Config key: `Transitland:ApiKey`
- Transitland vector tiles base URL key: `Transitland:TilesBaseUrl` (default: `https://transit.land/api/v2/tiles`)
- The app always uses bundled local `mapstyle.json` for base map style and overlays Transitland vector tile layers using API key header auth.

### 1. Ticketing System

- **Universal account and wallet:**  
  Register once, top up securely, and use the balance for any mode/operator.

- **Modular backend:**  
  Plugin/adapters let operators (public, private, regional, national, even international) quickly integrate their ticket APIs.  
  Users buy tickets as guests—no need for local accounts everywhere.

- **Unified ticket storage:**  
  All ticket formats (QR, barcode, PDF, link, SMS, NFC, etc.), stored and displayed in one wallet, with purchase history and instructions.

---

### 2. Scheduling & Journey Planning

- Integrated journey planner/map (future):  
  Combines GTFS/multimodal APIs for real-time routes, options, and accessibility info.
- Users can plan complex trips across cities, countries, and operators without switching apps.

---

### 3. Branding & Localization

- Dynamic branding:  
  When users select a location, UI adapts to local branding (logo, colors, city imagery) if available.
- Location-aware; localized languages and content.

---

## Extensible Features & Roadmap

### Phase 1: Core Public Transit
- City trams, buses, metro
- City/regional trains
- City-owned bikes (e.g. Bajk/Nextbike)

### Phase 2: Urban Mobility Extras
- E-scooters (city-owned/integrated first, private later)

### Phase 3: Regional & Private Operators
- Intercity/private buses (e.g. **Flixbus**, BlaBlaBus)
- Private rail companies, regional and national trains (where API allows)
- Ferry and maritime transport

### Phase 4: Long-Distance & Air Travel
- Flight map integration and journey planning (tickets are very complex, but at minimum: show schedules, allow linking out)
- International train operators

### Future: Unified Travel & Mobility Ecosystem
- One app for tickets, journey planning, and multimodal travel across cities, countries, and continents.
- Avoid fragmentation—no more dozens of apps for every schedule or ticket.
- The "Netflix approach" for transport: Integrate all modes/operators in one place.

---

## Technical Structure

- **Backend:** Modular plugin architecture for rapid integration of new ticketing APIs, mobility modes, and journey planners.
- **Frontend:** Dynamic UI for ticket management, trip planning, branding, and multimodal display.
- **API:** Endpoints for wallet actions, ticketing, trip requests, operator onboarding, and feature modules.

---

## Operator/Provider Integration

Cities, public authorities, and commercial operators can:
- Request and manage integration by providing API access/documentation.
- Maintain their plugin/adapters for ticketing, journey data, or branding.
- Expand user reach effortlessly—users can access their services via unified app.

---

## Planned Roadmap

1. **Modular ticketing platform & wallet** (core public transport)
2. **Micromobility/bike/scooter integration**
3. **Journey planner/map module**
4. **Regional/private bus/train integration (e.g. Flixbus)**
5. **Air travel & long-distance planning**
6. **Unified ecosystem for all mobility modes—no fragmentation**

---

## Contribution & Contact

- Operators and contributors: see [docs](docs/) for integration, plugin development, and roadmap features.
- For partnerships or technical questions, contact maintainers or file an issue.

---

## License

> *Add applicable license information here (open/commercial/etc.)*
