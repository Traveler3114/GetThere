GETTHERE ARCHITECTURE PLAN (OTP FIRST PHASE + FUTURE READY)

IMPORTANT INSTRUCTION FOR AGENT (READ FIRST)
Before implementing anything, you must:
- Ask for clarification on future plans and constraints
- Confirm architectural assumptions before coding
- Do not start implementation immediately
- Understand long-term goals (AI routing, flights, multi-region scaling)
- Prefer asking questions over making assumptions

Goal: avoid early design mistakes that would require refactoring later.

GOAL
Implement OpenTripPlanner (OTP) as the single source of truth for transit data:
- Stops
- Schedules
- Routes
- Realtime delays (when available)

The system must be designed so future features can be added without refactoring core architecture:
- Flights
- Multi-region routing
- AI route optimization
- GBFS (bikes/scooters)

CORE PRINCIPLE
OTP = regional transit engine (not global system)
GetThere API = global orchestration layer

PHASE 1 (NOW) — OTP INTEGRATION

OTP ROLE
OTP is responsible for:
- All transit stops
- All schedules
- All routes
- All rail/bus/tram/metro data
- Realtime delays (GTFS-RT)

Do not:
- Parse GTFS in backend
- Use Transitland
- Run vehicle tracking
- Store schedules in DB

OTP is the only transit data source.

DATA FLOW
Frontend
↓
GetThere API (ASP.NET)
↓
OtpClient (GraphQL)
↓
OpenTripPlanner

OTP SETUP (CURRENT SCOPE)
- Single OTP instance (EU)
- Load GTFS feeds (ZET, HZPP, LPP, OBB)
- Enable GTFS-RT where available (delays only)

BACKEND RESPONSIBILITIES

OtpClient (GraphQL only)
- GetStops(countryId)
- GetStopSchedule(stopId, countryId)
- GetRoutes(countryId)
- HealthCheck()

ITransitProvider (abstraction)
- GetStops(countryId)
- GetStopSchedule(stopId, countryId)
- GetRoutes(countryId)
- HealthCheck()
- Route planning methods defined but not implemented yet

OtpTransitProvider
- Implements ITransitProvider
- Uses OtpClient
- Maps OTP responses to API DTOs

OperatorManager
- Thin orchestration layer
- No parsing logic
- No data ownership
- Only mapping and coordination

FRONTEND BEHAVIOR
- Load all stops per selected country
- No viewport filtering
- On stop click fetch schedule
- Display delays if available
- No vehicle tracking

Caching:
- Stored on frontend device only
- Backend remains stateless

REMOVE COMPLETELY
- GTFS static parsers
- GTFS-RT parsers
- Transitland integration
- Vehicle tracking system
- Background polling services
- Schedule persistence logic

REQUIRED ABSTRACTION
- Introduce ITransitProvider
- Use OtpTransitProvider
- Do not couple OTP directly to business logic

FUTURE REQUIREMENTS (DO NOT IMPLEMENT NOW)

Multi-region OTP
- OTP-EU
- OTP-US
- OTP-ASIA
- Instance mapping must exist now

Global routing
Trips will include:
- OTP (origin)
- Flight API
- OTP (destination)

AI routing layer
- Fastest route
- Cheapest route
- Balanced route
- AI scoring

GBFS system
- Bikes and scooters
- Separate from OTP
- Adapter per provider

ARCHITECTURE TARGET STATE
GetThere API
↓
Transit Orchestrator (future)
↓
OTP | Flights | GBFS

SCALABILITY RULES

Do not:
- Create per-country OTP instances
- Hardcode transport logic
- Couple frontend to OTP schema

Do:
- Use provider abstraction
- Keep OTP isolated behind client and provider

SUCCESS CRITERIA (PHASE 1)
- OTP running
- Stops load correctly
- Schedules returned
- Realtime delays shown
- No GTFS parsing remains
- Clean abstraction layer exists

API BEHAVIOR RULES
- GraphQL only for OTP
- Return delay = null if realtime unavailable
- Breaking API changes allowed
- DTOs act as stable contracts

ONE LINE SUMMARY
Replace all GTFS and Transitland logic with OTP behind a clean provider abstraction and structure the system for future multi-region, flights, GBFS, and AI routing without refactoring.

---

IMPLEMENTATION AND PROGRESS DOCUMENTATION

DECISIONS MADE
- GraphQL only
- Instance mapping exists
- Route planning methods defined but not implemented
- Transit orchestrator introduced now
- Frontend caching only
- Transitland removed
- Delay null when no realtime
- Breaking changes allowed
- Minimal abstraction

WHAT NEEDS TO BE DONE
- Integrate OTP GraphQL
- Implement OtpClient
- Create ITransitProvider
- Implement OtpTransitProvider
- Rewrite OperatorManager
- Remove GTFS logic
- Remove Transitland
- Update API endpoints
- Connect frontend

HOW TO EXTEND

Adding new region
1. Deploy new OTP instance
2. Add to mapping
3. Extend routing layer

Adding flights
1. Create flight provider
2. Implement API adapter
3. Extend orchestrator

Adding GBFS
1. Create GBFS provider
2. Add provider per vendor
3. Integrate into orchestrator

Adding AI routing
1. Implement route planning
2. Add scoring logic
3. Rank routes in orchestrator

NOTES
- Keep backend stateless
- Avoid overengineering
- Always use providers
- Do not bypass abstraction

NEXT PHASE
- Add orchestrator logic
- Enable route planning
- Start multi-region
- Prepare flight integration

FINAL REMINDER
Before any architectural decision:
- Ask about scaling plans
- Confirm assumptions
- Think long-term
