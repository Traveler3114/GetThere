# AGENTS.md

This file provides context and guidance to AI coding assistants working with this repository.

## Project Overview

**GetThere** is a cross-platform public transport and urban mobility app built with .NET MAUI (mobile frontend) and ASP.NET Core (backend API). It provides real-time transit tracking, bike-sharing stations, mock ticket purchasing, and a wallet system.

**Tech Stack:**
- Frontend: .NET MAUI 10 (Android, iOS, macOS, Windows) — C# / XAML
- Backend: ASP.NET Core 10 (REST API) — C#
- Database: SQL Server via Entity Framework Core 10
- Auth: ASP.NET Identity + JWT Bearer tokens
- Map: MapLibre GL JS (rendered in a WebView), OpenFreeMap tiles
- Transit data: GTFS (static) + GTFS-RT protobuf (realtime)
- Bike sharing: Nextbike Live JSON API

---

## Solution Structure

```
GetThere/           # .NET MAUI mobile app
GetThereAPI/        # ASP.NET Core REST API
GetThereShared/     # Shared DTOs, enums, and common types (referenced by both)
```

### GetThere (MAUI App)

```
Pages/              # ContentPages (LoginPage, MapPage, ShopPage, TicketsPage, ProfilePage, etc.)
Services/           # HTTP clients wrapping API endpoints (AuthService, OperatorService, ShopService, etc.)
Components/         # Reusable XAML components (AnimatedBackground, ModernLoader, BreathingBackground)
Helpers/            # PageUtility, AuthenticatedHttpHandler, value converters
State/              # Singleton app state (CountryPreferenceService, MockTicketStore)
Behaviors/          # XAML behaviors (AnimatedGradientBehavior, BackgroundAnimationService)
Map/                # map.html, map.js, map.css, mapstyle.json (injected into WebView)
Shells/             # AppShell.xaml (main tabs), LoginShell.xaml (auth flow)
Resources/          # Fonts, images, styles, colors, app icon, splash
```

### GetThereAPI (Backend)

```
Controllers/        # REST controllers (AuthController, OperatorController, MapController, etc.)
Managers/           # Business logic (OperatorManager, RealtimeManager, StaticDataManager, etc.)
Parsers/
  Realtime/         # GTFS-RT parsers (GtfsRtProtoParser, GtfsRtJsonParser, SiriParser, RestJsonParser)
  Static/           # GTFS static parsers (GtfsStaticParser, IStaticDataParser)
  Mobility/         # Bike share parsers (NextbikeParser, IMobilityParser)
Entities/           # EF Core entity classes
Data/               # AppDbContext
Migrations/         # EF Core migrations
```

### GetThereShared

```
Dtos/               # Request/response DTOs shared between API and app
Enums/              # Shared enums (TicketStatus, TicketFormat, PaymentStatus, WalletTransactionType)
Common/             # OperationResult<T> wrapper used by all API responses
```

---

## Key Architectural Patterns

### API Response Wrapper
All API endpoints return `OperationResult<T>` defined in `GetThereShared/Common/OperationResult.cs`:
```csharp
{ Success: bool, Message: string, Data: T? }
```
Always use this wrapper — never return raw data or plain HTTP status codes alone.

### Service Registration (MAUI)
Services in `GetThere.Services` namespace are auto-registered by reflection in `MauiProgram.cs`. To add a new service, simply create a class in that namespace with a constructor taking `HttpClient`. The `AuthenticatedHttpHandler` middleware automatically attaches the JWT token to all requests except `AuthService`.

### Manager Registration (API)
Managers in `GetThereAPI.Managers` namespace are auto-registered as scoped services by reflection in `Program.cs`. Exceptions: `StaticDataManager`, `RealtimeManager`, and `MobilityManager` are singletons (they hold in-memory caches) and registered manually as both singleton and hosted service.

### Parser Factory Pattern
Each feed type (realtime, static, mobility) has a factory (`RealtimeParserFactory`, `StaticParserFactory`, `MobilityParserFactory`) that resolves the correct parser from a string stored in the database (`TransitOperator.RealtimeFeedFormat`, etc.). To add a new feed format:
1. Create `YourFormatParser : IRealtimeParser` (or the appropriate interface)
2. Add one `case` in the factory
3. No other changes needed

### In-Memory Caching
- `StaticDataManager` — caches GTFS stops, routes, and trip maps loaded from ZIP files
- `RealtimeManager` — caches live vehicle positions, updated every 10 seconds
- `MobilityManager` — caches bike station lists, updated every 2 minutes

These singletons are pre-populated on startup via `InitialiseAsync()` called from a background `Task.Run` in `Program.cs` so the server starts accepting requests immediately.

### Country Filtering
The app stores a selected country ID in `CountryPreferenceService` (MAUI `Preferences`). Most API endpoints accept an optional `?countryId=` query parameter to scope results. When no country is selected, all data is returned.

### Mock Tickets
The shop uses a hardcoded mock catalogue in `MockTicketController.cs`. Purchased mock tickets deduct from the user's wallet (real DB transaction), are saved to the `Tickets` table (for transit operators with a DB row), and are also kept in `MockTicketStore` (in-memory singleton) for the current session. Bajs/Nextbike tickets are in-memory only (no `TransitOperator` DB row).

---

## Database

SQL Server, managed with EF Core migrations.

**Connection string** (development): `Server=localhost;Database=GetThereDB_v2;Trusted_Connection=True;TrustServerCertificate=True`

### Key Entities
| Entity | Purpose |
|---|---|
| `AppUser` | ASP.NET Identity user |
| `Wallet` | One per user, holds balance |
| `WalletTransaction` | Top-up, ticket purchase, or refund |
| `Ticket` | Purchased transit ticket |
| `Payment` | Payment record linked to wallet |
| `TransitOperator` | Bus/tram/train operator with GTFS feed URLs |
| `MobilityProvider` | Bike/scooter provider with API config |
| `Country` / `City` | Geographic hierarchy |
| `TransportType` | GTFS route type metadata (icon, color) |
| `PaymentProvider` | Payment gateway config |

### Migration Commands
```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project GetThereAPI

# Apply migrations to database
dotnet ef database update --project GetThereAPI

# Rollback one migration
dotnet ef database update <PreviousMigrationName> --project GetThereAPI
```

---

## Running the Project

### API (GetThereAPI)
```bash
cd GetThereAPI
dotnet run
# Listens on https://localhost:7230 and http://localhost:5000
# Scalar API reference: https://localhost:7230/scalar/v1
```

On startup the API:
1. Applies EF Core migrations (if configured)
2. Loads GTFS feeds for all `TransitOperator` rows in the database
3. Starts background polling for GTFS-RT (every 10 s) and bike stations (every 2 min)

### MAUI App (GetThere)
Run from Visual Studio or via:
```bash
# Android emulator
dotnet build -t:Run -f net10.0-android

# Windows
dotnet build -t:Run -f net10.0-windows10.0.19041.0
```

The API base URL is set per platform in `MauiProgram.GetApiBaseUrl()`:
- Android emulator: `https://10.0.2.2:7230/`
- iOS simulator / macOS / Windows: `https://localhost:7230/`

SSL certificate validation is bypassed in development (`ServerCertificateCustomValidationCallback`). Remove before production.

---

## API Endpoints Reference

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/auth/register` | No | Register new user |
| POST | `/auth/login` | No | Login, returns JWT |
| GET | `/countries` | No | List all countries |
| GET | `/operator` | No | List transit operators |
| GET | `/operator/ticketable` | No | Operators available for ticket purchase |
| GET | `/operator/stops` | No | All stops (optional `?countryId=`) |
| GET | `/operator/routes` | No | All routes (optional `?countryId=`) |
| GET | `/operator/vehicles` | No | Live vehicles (optional `?countryId=`) |
| GET | `/operator/stops/{id}/schedule` | No | Today's departures for a stop |
| GET | `/operator/trips/{id}` | No | Trip stop sequence with delays |
| GET | `/operator/transport-types` | No | Transport type icons and colors |
| GET | `/map/features` | No | All map features (stops + vehicles + bikes) |
| GET | `/map/bike-stations` | No | Bike stations (optional `?countryId=`) |
| GET | `/wallet` | Yes | User's wallet balance |
| GET | `/wallet/transactions` | Yes | Wallet transaction history |
| POST | `/payment/topup` | Yes | Top up wallet |
| GET | `/payment/providers` | Yes | Available payment providers |
| GET | `/ticket` | Yes | User's purchased tickets |
| GET | `/mock-tickets/{id}/options` | No | Ticket options for operator |
| POST | `/mock-tickets/{id}/purchase` | Yes | Purchase a mock ticket |

---

## Map Architecture

The map is a self-contained HTML/JS/CSS bundle loaded into a `WebView` (`MapPage.xaml.cs`). On page load, C# injects:
- `window._API_BASE` — API base URL
- `window._MAP_STYLE` — MapLibre style JSON (from `mapstyle.json`)
- `window._TRANSPORT_TYPES` — transport type config fetched from API
- `window._ICON_DATA` — stop icons as base64 strings (prefetched to avoid CORS issues)

**C# → JS calls** use `EvaluateJavaScriptAsync` with base64-encoded JSON to avoid escaping issues:
```csharp
var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
await MapWebView.EvaluateJavaScriptAsync($"myFunction(JSON.parse(atob('{b64}')))");
```

**JS → C# calls** use a polling mechanism: JS sets `window._pendingMsg` to a string prefixed with the action type (`stopSchedule:`, `tripDetail:`). C# polls this every 300 ms.

---

## Adding New Features — Common Patterns

### New API endpoint
1. Add method to the appropriate `*Controller.cs`
2. Add business logic to the corresponding `*Manager.cs`
3. Add DTOs to `GetThereShared/Dtos/`
4. Add service method to the MAUI `*Service.cs`

### New transit operator
Insert a row into `TransitOperators` with the correct `GtfsFeedUrl`, `RealtimeFeedFormat`, and auth config. The server will load it on next startup (or restart).

### New bike/scooter provider
1. Create a parser implementing `IMobilityParser`
2. Add a case to `MobilityParserFactory`
3. Insert a row into `MobilityProviders`

### New GTFS-RT feed format
1. Create a parser implementing `IRealtimeParser`
2. Add a case to `RealtimeParserFactory`
3. Set the `RealtimeFeedFormat` column on the relevant `TransitOperator` row

### New MAUI page
1. Create `MyPage.xaml` + `MyPage.xaml.cs` in `GetThere/Pages/`
2. Register a route in the appropriate Shell, or use `Routing.RegisterRoute`
3. The page constructor dependencies are auto-resolved by DI

---

## Code Style Conventions

- All API responses use `OperationResult<T>` — never return raw objects
- Nullable reference types are enabled (`#nullable enable`) — annotate accordingly
- Async methods always use `Async` suffix and return `Task<T>`
- Services return `null` on failure and log via `Trace.WriteLine` — callers handle null gracefully
- XAML pages use `DisplayAlertAsync` / `DisplayPromptAsync` (extension methods on `ContentPage`)
- Entity Framework queries use `AsNoTracking()` for read-only operations
- Enums are stored as strings in the database (via `EnumToStringConverter` in `AppDbContext`)
- Seed data is defined in `AppDbContext.OnModelCreating` — note the comment to remove before production

---

## Security Notes

- JWT secret is in `appsettings.json` — **move to environment variables or secrets manager before production**
- `ServerCertificateCustomValidationCallback` bypasses SSL in MAUI — **remove before production**
- Seed data includes mock payment API keys — **remove `HasData` calls before production**
- All `[Authorize]` endpoints extract the user ID from the JWT `sub` claim via `User.FindFirstValue(JwtRegisteredClaimNames.Sub)`

---

## Git Workflow

- Work on feature branches, not directly on main
- Migration files are auto-generated — commit them alongside the entity changes that triggered them
- The `AppDbContextModelSnapshot.cs` is auto-generated by EF Core — do not edit manually

---

## Known Limitations & TODOs

- Mock ticket catalogue (`MockTicketController.cs`) is hardcoded — needs a real ticketing API integration
- `TicketApiBaseUrl` and `TicketApiKey` on `TransitOperator` entities are empty strings — not yet used
- LPP (Ljubljana) is in the mock ticket catalogue but its `TransitOperator` DB ID changed between migrations — verify mapping in `MockTicketController.DbTransitOperatorIds`
- SSL certificate bypass must be removed and proper certificates configured for production
- Seed data (countries, cities, operators) should be extracted to a separate seeder or migration script before production deployment
