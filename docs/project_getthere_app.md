# GetThere (MAUI App) — Full Code Documentation

## 1) Project role in the solution
`GetThere` is the end-user client application built with .NET MAUI.

### What it owns
- UI rendering and navigation flow.
- Client-side session/token handling.
- Calling backend APIs.
- Local client preferences (selected country).
- Embedded map UX (WebView + MapLibre JS bundle).

### What it does not own
- Ticketing business truth.
- Wallet/payment business logic.
- Transit and mobility data computation.
- OTP/GTFS data transformation.

Those responsibilities are delegated to backend services.

---

## 2) Build/runtime fundamentals
- Project file: `GetThere/GetThere.csproj`
- Frameworks:
  - `net10.0-android`
  - `net10.0-ios` (non-Linux)
  - `net10.0-maccatalyst` (non-Linux)
  - `net10.0-windows10.0.19041.0` (Windows)
- App type: MAUI single-project app (`<UseMaui>true</UseMaui>`)

### Key NuGet packages
- `Microsoft.Maui.Controls`
- `CommunityToolkit.Maui`
- `SkiaSharp.Skottie`
- `SkiaSharp.Views.Maui.Controls`
- `Microsoft.Extensions.Http`
- `Google.Protobuf`

### Shared dependency
- References `GetThereShared` for DTOs/result wrappers used in API communication.

---

## 3) Dependency injection and startup composition
Primary composition root: `GetThere/MauiProgram.cs`

### Startup flow
1. Creates MAUI app builder.
2. Registers toolkit/skia/fonts.
3. Resolves API base URL by platform:
   - Android emulator: `https://10.0.2.2:7230/`
   - iOS/Mac/desktop: `https://localhost:7230/`
4. Registers `HttpClient` pipelines:
   - `AuthService` uses direct client.
   - Other services use `AuthenticatedHttpHandler` to inject bearer token.
5. Uses reflection to auto-register:
   - all classes in `GetThere.Services` (except `AuthService` and handler),
   - all `ContentPage` classes in `GetThere.Pages`.
6. Registers shells and local state services as singletons.

### Important implementation detail
A permissive cert callback is currently configured in `MauiProgram` (`ServerCertificateCustomValidationCallback = ... => true`) and used by service clients. This is suitable only for development; production hardening should remove bypass behavior.

---

## 4) Shell and navigation model

### App shell hierarchy
- `App.xaml.cs`
  - initial window = `LoginShell`.
  - static helpers switch shell root:
    - `GoToApp()` -> `AppShell`
    - `GoToLogin()` -> `LoginShell`
    - `GoToRegistration()` route handling for registration screen.

### Login shell
- Files:
  - `Shells/LoginShell.xaml`
  - `Shells/LoginShell.xaml.cs`
- Contains login content route and registration route registration.

### Main app shell
- File: `Shells/AppShell.xaml`
- Tab layout (bottom tab bar):
  - Profile (`route=profile`)
  - Map (`route=map`)
  - Shop (`route=shop`)
  - Tickets (`route=tickets`)

---

## 5) Service layer (client API integration)
Location: `GetThere/Services`

### AuthService
- `LoginAsync(LoginDto)` -> POST `/auth/login`
- `RegisterAsync(RegisterDto)` -> POST `/auth/register`
- Persists token in secure storage key `jwt_token`.
- Includes token claim decode helpers for client display convenience.

### OperatorService
Consumes transit/map-facing endpoints:
- `/operator`
- `/operator/stops`
- `/operator/routes`
- `/operator/stops/{stopId}/schedule`
- `/operator/transport-types`
- `/map/bike-stations`

Also exposes `GetApiBaseUrl()` for map JS runtime injection.

### CountryService
- GET `/countries`
- Used by settings/country selector UX.

### ShopService
- GET `/operator/ticketable`
- GET `/mock-tickets/{operatorId}/options`
- POST `/mock-tickets/{operatorId}/purchase`

### WalletService
- GET `/wallet`
- GET `/wallet/transactions`

### PaymentService
- GET `/payment/providers`
- POST `/payment/topup`

### TicketService
- GET `/ticket`

---

## 6) Authenticated request pipeline
File: `Helpers/AuthenticatedHttpHandler.cs`

### Behavior
- On every outgoing request (except auth pipeline), reads token from `AuthService` secure storage.
- If present, adds:
  - `Authorization: Bearer <jwt>`
- Enables `[Authorize]` endpoints without requiring each service method to attach headers manually.

---

## 7) Local state services

### CountryPreferenceService
File: `State/CountryPreferenceService.cs`
- Stores `selectedCountryId` and `selectedCountryName` in MAUI `Preferences`.
- Provides:
  - `HasSelection`
  - get/set/clear methods.

### MockTicketStore
File: `State/MockTicketStore.cs`
- In-memory session store for purchased mock tickets.
- Singleton lifetime, newest-first read projection.
- Non-persistent by design.

---

## 8) Map subsystem (detailed)

### Files
- `Pages/MapPage.xaml.cs` (C# orchestration)
- `Map/map.html`
- `Map/map.css`
- `Map/map.js`
- `Map/mapstyle.json`

### Startup sequence for map page
1. Load `map.html` from app package.
2. Inline CSS (`map.css`).
3. Inject `window._API_BASE`.
4. Inject `window._MAP_STYLE` from `mapstyle.json`.
5. Fetch transport type config from backend (`/operator/transport-types`).
6. Prefetch icon images from backend `/images/{file}` and inject base64 payloads into `window._ICON_DATA`.
7. Inline JS (`map.js`).
8. Assign final HTML to `WebView`.
9. Wait for JS map readiness handshake (`window._mapReady`).
10. Load static datasets in parallel:
   - stops,
   - routes,
   - bike stations.
11. Request geolocation and call JS `updateMapLocation`.
12. Start JS message polling timer.

### JS/C# bridge protocol
- JS places messages in `window._pendingMsg`.
- C# polling loop reads and clears it.
- Current message contract includes:
  - `stopSchedule:<stopId>`

### JS rendering capabilities present in `map.js`
- Sources/layers for:
  - stops,
  - vehicles (infrastructure present),
  - bike stations,
  - active route line.
- Stop schedule panel rendering with delay/realtime badges.
- Bike station panel handling.
- Vehicle render functions exist; current backend phase primarily uses stop/route/schedule and bike stations.

---

## 9) Page inventory (functional responsibility)
Location: `GetThere/Pages`
- `LoginPage` / `RegistrationPage`: auth entry.
- `MapPage`: transit map UX and schedule-on-stop-click flow.
- `ShopPage`: ticketable operators + options + purchase.
- `TicketPurchasePage`: purchase process UI.
- `TicketsPage`: user ticket list.
- `ProfilePage`: profile info.
- `SettingsPage`: country and preferences behavior.
- `MockTicketConfirmationPage`: displays mock purchase result.

---

## 10) How to add future code safely in this app

### Rule A — keep strict layering
- UI pages should orchestrate, not contain backend business logic.
- All API calls belong in `Services` classes.
- DTO shape changes must come from `GetThereShared`.

### Rule B — add features in contract-first order
1. Backend endpoint behavior.
2. Shared DTO contract update.
3. MAUI service method.
4. Page/ViewModel wiring.
5. UX and error-state handling.

### Rule C — map changes should be protocol-based
- If adding new JS↔C# events, define explicit message prefix contracts.
- Keep marshaling centralized in `MapPage` methods.
- Avoid hidden ad-hoc JS global interactions.

### Rule D — resilience and UX
- Continue null-safe handling pattern (`null` from service means non-fatal UI fallback).
- Avoid hard crashes from transient API/network errors.

---

## 11) Future engineering improvements for this project
- Introduce formal MVVM view models for each page to reduce code-behind complexity.
- Replace stringly-typed JS bridge messages with a structured message envelope.
- Add integration tests for:
  - login/auth header propagation,
  - map data bootstrap,
  - shop purchase happy/failure paths.
- Harden TLS behavior for production builds (remove cert bypass).
- Introduce API client abstractions per domain if endpoint count grows significantly.
