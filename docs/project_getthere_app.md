# GetThere (MAUI App) — Detailed Code Documentation

## 1. Purpose of this project
`GetThere` is the cross-platform client app (.NET MAUI) that users interact with.  
It is responsible for:
- Authentication UX (login/registration),
- Wallet/tickets/shop screens,
- Transit map rendering (via embedded WebView + MapLibre),
- Calling backend APIs through typed service classes.

It does **not** own business truth for tickets, wallets, or transit data. It consumes backend contracts from `GetThereShared` DTOs.

---

## 2. Core architecture

### Composition root
- File: `GetThere/MauiProgram.cs`
- Responsibilities:
  - Configures app host, fonts, UI libraries, and dependency injection.
  - Registers all `GetThere.Services` classes dynamically.
  - Registers all pages dynamically.
  - Sets API base URL by target platform (`10.0.2.2` for Android emulator, `localhost` elsewhere).

### App startup and shell switching
- File: `GetThere/App.xaml.cs`
- Starts at `LoginShell`.
- Provides static navigation helpers:
  - `GoToApp()`
  - `GoToLogin()`
  - `GoToRegistration()`

### Service layer
- Folder: `GetThere/Services`
- Pattern:
  - One class per backend area (`AuthService`, `OperatorService`, `ShopService`, etc.).
  - Uses `HttpClient`.
  - Returns DTO/`OperationResult` payloads from `GetThereShared`.

### Map subsystem
- Files:
  - `GetThere/Pages/MapPage.xaml.cs`
  - `GetThere/Map/map.html`
  - `GetThere/Map/map.css`
  - `GetThere/Map/map.js`
- Flow:
  - C# loads/inlines HTML/CSS/JS into WebView.
  - C# injects runtime config (`window._API_BASE`, map style, transport types, icon data).
  - C# calls JS functions to render stops/routes/stations.
  - JS sends user events back to C# through a shared message slot (`window._pendingMsg` polled by C# timer).

---

## 3. Major code flows

### Authentication flow
1. UI calls `AuthService.LoginAsync`.
2. API returns `OperationResult<UserDto>` with JWT.
3. JWT is saved in `SecureStorage`.
4. `AuthenticatedHttpHandler` attaches token to subsequent API calls.

### Map data flow
1. `MapPage` waits for WebView navigation + JS map readiness.
2. Calls `OperatorService.GetStopsAsync`, `GetRoutesAsync`, and `GetBikeStationsAsync`.
3. Sends data into JS rendering functions.
4. On stop click, JS emits `stopSchedule:<stopId>`.
5. C# fetches schedule and injects response back to JS (`renderStopSchedule`).

### Shop/ticket mock purchase flow
1. Shop page uses `ShopService` to list ticketable operators and options.
2. Purchase endpoint returns mock ticket payload.
3. Tickets and wallet history screens query backend data via `TicketService`/`WalletService`.

---

## 4. How this project should be extended (future code standards)

### Keep the current layering
- UI pages should remain presentation-focused.
- All HTTP/API logic must stay in `Services`.
- Shared request/response contracts must come from `GetThereShared` (never duplicate DTOs locally).

### Add features through services first
For any new screen:
1. Add/extend backend endpoint.
2. Add/extend shared DTO.
3. Add/extend MAUI service method.
4. Bind page/viewmodel to service.

### Map feature expansion rules
- Add new map feature types by adding backend DTOs and JS render functions.
- Keep C#↔JS bridge functions explicit (single-purpose methods).
- Avoid embedding business rules in JS if backend can compute canonical values.

### Security and reliability rules
- Keep tokens in secure storage only.
- Do not bypass cert validation outside development scenarios.
- Handle null/failed API responses gracefully in UI (existing pattern).

---

## 5. Recommended next code improvements
- Introduce ViewModels for cleaner page logic separation.
- Add typed API clients per domain (still behind service classes).
- Add map message protocol constants/shared schema to avoid stringly-typed message events.
- Add automated UI/integration tests for login, map load, and ticket purchase happy paths.
