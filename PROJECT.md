# Project context

## Stack
- **Language**: C# 12+
- **Frontend**: .NET MAUI 10 (Android, iOS, macOS, Windows) — XAML
- **Backend**: ASP.NET Core 10 (REST API)
- **Database**: SQL Server via Entity Framework Core 10
- **Auth**: ASP.NET Identity + JWT Bearer tokens (+ refresh tokens)
- **Map**: MapLibre GL JS (in WebView), OpenFreeMap tiles
- **Transit data**: GTFS static + GTFS-RT protobuf (realtime) via OpenTripPlanner GraphQL
- **Bike sharing**: Nextbike Live JSON API
- **Journey planning**: OpenTripPlanner (OTP) GraphQL

## Solution structure
```
GetThere/
├── Pages/              # MAUI pages (Map, Shop, Tickets, Profile, Login, etc.)
├── Services/           # MAUI HTTP service clients
├── Components/         # Reusable XAML components
├── Behaviors/          # XAML behaviors
├── Shells/             # AppShell / LoginShell
├── Helpers/            # AuthenticatedHttpHandler, PageUtility
├── Resources/          # AppIcon, Fonts, Images, Splash, Styles
├── Platforms/          # Platform-specific (Android, iOS, MacCatalyst, Windows)
│   └── Map/            # MapLibre HTML/JS/CSS bundle
└── MauiProgram.cs      # DI setup, API base URL per platform

GetThereAPI/
├── Program.cs          # Startup, DI, middleware, global error handler
├── Common/             # SqlHelper (database error utilities)
├── Controllers/        # REST API endpoints (thin — forward to managers)
├── Managers/           # All business logic
├── Mapping/            # Static DTO mappers
├── Sdk/                # Adapter interfaces (ITicketingAdapter), registry
├── Contracts/          # Shared (not here — in GetThereShared.Contracts)
├── Data/               # AppDbContext
├── Entities/           # EF Core entity classes
├── Enums/              # MobilityType, MobilityFeedFormat
├── Parsers/Mobility/   # Nextbike adapter, parser factory
├── Transit/            # OTP client, provider, router, orchestrator
├── Migrations/         # EF Core migrations
└── wwwroot/            # Static files, admin pages

GetThereShared/
├── Common/             # OperationResult<T>, PagedResult<T>
├── Contracts/          # Request/response DTOs by domain
└── Enums/              # TicketFormat, TicketStatus, PaymentStatus, WalletTransactionType

TransitInfoAPI/
├── Program.cs          # Startup, DI, middleware
├── Common/             # OperationResult
├── Controllers/        # REST API endpoints (reconciliation, feeds, stations)
├── Services/           # Business logic (ReconciliationService)
├── Core/               # Domain logic interfaces/abstractions
├── Data/               # TransitDbContext
├── Entities/           # EF Core entity classes (CanonicalStation, Feed, etc.)
├── Enums/              # StationType, ReconciliationStatus
├── Models/             # DTOs (ReconciliationDto)
├── Migrations/         # EF Core migrations
├── Proto/              # Protobuf definitions
└── wwwroot/            # Static files
```

## Dependencies
- Entity Framework Core 10
- ASP.NET Identity
- JWT Bearer authentication
- MapLibre GL JS
- GTFS / GTFS-RT protobuf
- Nextbike JSON API
- OpenTripPlanner GraphQL
- SkiaSharp (MAUI)
- CommunityToolkit.Maui

## Conventions — Code Style

### Namespaces
Always file-scoped: `namespace GetThereAPI.X.Y;`
```csharp
// Good
namespace GetThereAPI.Managers;

// Bad
namespace GetThereAPI.Managers { }
```

### Constructors
Single-line block body for simple field assignments:
```csharp
public BookingManager(AppDbContext db) { _db = db; }
```
Use multi-line only when the constructor contains logic beyond assignments (e.g., reading config sections).

### Collection initialization
Use `[]` collection expressions everywhere:
```csharp
private List<string> items = [];
public ICollection<Ticket> Tickets { get; set; } = [];
return [];
```
Avoid `new List<T>()` in new code.

### Null checks
Use `is null` / `is not null` pattern:
```csharp
if (user is null) return ...;
if (user is not null) ...
```
Avoid `== null` / `!= null` except in lambda expressions where pattern matching isn't available.

### Private fields
```csharp
private readonly AppDbContext _db;
```

### String defaults
```csharp
public string Name { get; set; } = string.Empty;
```
Never `= null!` or `= ""`.

### Using directives
Order: `System.*` → `Microsoft.*` → third-party → project (`GetThereAPI.*` / `GetThereShared.*`).

### Async pattern
- All DB access and business logic is fully async
- `CancellationToken ct = default` as the **last** parameter on all async methods in the API
- Always pass `ct` to EF Core methods
- MAUI services don't use `CancellationToken` (HTTP timeouts handle cancellation)

### Error handling
- **Global exception handler** in `Program.cs` catches and logs all unhandled exceptions, returns `500` with `OperationResult<string>.Fail`
- **Transaction catch blocks**: `catch { await dbTx.RollbackAsync(ct); throw; }` — never swallow
- **Controllers never catch exceptions** — let them bubble to the global handler
- **Never silently swallow exceptions** — if you catch, you must log or rethrow
- **SqlHelper** (`GetThereAPI/Common/SqlHelper.cs`) for database error utilities:
  - `GetUserFriendlyMessage(SqlException)` → human-readable message
  - `IsUniqueConstraintViolation(SqlException)` → detect duplicate key violations
  - `IsDeadlock(SqlException)` → detect deadlock for potential retry

### DTO / Contract naming
| Element | Convention | Example |
|---------|-----------|---------|
| Request DTOs | `{Action}{Domain}Request` (records) | `PurchaseTicketRequest` |
| Response DTOs | `{Domain}Response` (classes) | `TicketResponse` |
| Contracts file | `{Domain}Contract.cs` | `TicketContract.cs` |

### Mappers
- Static classes in `GetThereAPI/Mapping/` folder: `{Domain}Mapper` (e.g., `TicketMapper`)
- Manual field mapping methods (no AutoMapper)
- Names: `ToResponse()`, `ToEntity()`, `ToDto()` depending on direction
- Every source field must be explicitly mapped or commented why excluded

### TryParse over Parse
```csharp
// Good
return int.TryParse(claim, out var id) ? id : 0;

// Bad
return int.Parse(claim);
```

## Conventions — Architecture

### Manager pattern
- All business logic lives in **Manager** classes — never in controllers
- Managers inject `AppDbContext` and/or other dependencies via constructor
- Managers are concrete classes (no interfaces, except when needed for DI swapping)
- Manager naming: `{Domain}Manager` (e.g., `TicketManager`, `FeedManager`)

### Controllers
- Always annotated: `[ApiController]`, `[Route]`, `[Authorize]` (where needed)
- Success (2xx): return the DTO/resource directly (`Ok(dto)`)
- Error (4xx/5xx): return `Problem(statusCode, title)` standard RFC 7807 ProblemDetails
- Pagination: return `Ok(new Paginated<T>(items, total))` where `Paginated<T>` is a concrete record in `Models/Paginated.cs`
- Thin: receive input → call manager → forward result
- Never contain business logic

### Response conventions
| Status | Body | Use case |
|--------|------|----------|
| 200 | DTO or `List<T>` | GET single or non-paginated list |
| 200 | `{ data, total }` via `Paginated<T>` | GET paginated list |
| 200 | `{ message }` | Command with optional message (e.g., import result) |
| 201 | DTO | Created resource via `CreatedAtAction` |
| 204 | (empty) | Command success (PUT, DELETE, POST action) |
| 400 | ProblemDetails | Bad request / validation error |
| 404 | ProblemDetails | Resource not found |
| 409 | ProblemDetails | Conflict (duplicate) |
| 500 | ProblemDetails | Server error |

### Pagination
- Offset-based: `?page=1&perPage=50`
- `page` is 1-based (page 1 = items 1–50)
- Response body: `{ "data": [...], "total": <int> }` via `Paginated<T>` record
- `total` is the total matching items (not filtered by page)
- Non-paginated list endpoints return the array directly with no wrapper

### Manager return patterns
Managers return data directly (no envelope wrapper):
- Found → return the DTO or `List<T>`
- Not found → return `null` or throw `AppException` (controller maps to `NotFound()`)

### Exception pattern
- Managers throw `AppException(message, statusCode, errorCode?)` for expected failures
- Global middleware catches `AppException` and writes a `ProblemDetails` response
- Controllers never handle errors — they just call the manager and return the happy path

### Auto-registration
- MAUI services in `GetThere.Services` namespace are auto-registered by reflection in `MauiProgram.cs`
- API managers in `GetThereAPI.Managers` namespace are auto-registered as scoped by reflection in `Program.cs`
- Exceptions (explicitly registered): `MobilityManager` (singleton + hosted), `AdapterRegistry` (singleton)

## Conventions — Validation

### Validate in the manager, not the database
If a field has a restricted set of allowed values, validate it explicitly in the manager and return `OperationResult.Fail` with a clear message. Never rely on a SQL constraint violation to be the user-facing error. The database constraint is a safety net, not the validation layer.

```csharp
// Good
if (request.Amount <= 0)
    return OperationResult<WalletDto>.Fail("Amount must be greater than zero.");

// Bad — lets the SQL constraint throw a generic error
entity.Amount = request.Amount;
await _db.SaveChangesAsync(ct);
```

## Conventions — Data Integrity

### Hard delete is never used on operational records
Tickets, wallet transactions, payments, and their related records are **cancelled or deactivated**, never deleted. Deleting operational records destroys the audit trail.

- **Tickets**: set `Status = Cancelled`
- **Hard delete is only permitted** for configuration records that have never been used

### Enums over magic strings
All status and type fields use enums with `HasConversion<string>()` in `AppDbContext`:
```csharp
// In OnModelCreating — automated for all enum properties
var converterType = typeof(EnumToStringConverter<>).MakeGenericType(underlying);
property.SetValueConverter(converterType);
```

## Conventions — MAUI

### MVVM / Code-behind
- Pages use code-behind (no formal MVVM framework)
- Services are injected via constructor DI
- UI logic stays in pages; business logic stays in API managers

### WebView map integration
- C# → JS calls use `EvaluateJavaScriptAsync` with base64-encoded JSON
- JS → C# calls use polling: JS sets `window._pendingMsg`, C# polls every 300ms

### Display helpers
MAUI pages use `DisplayAlertAsync` / `DisplayPromptAsync` extension methods from `GetThere.Helpers.PageUtility`

## Key files
| File | Purpose |
|------|---------|
| `README.md` | Project vision, scope, roadmap |
| `GetThereShared/Common/PagedResult.cs` | Paginated list response (GetThereAPI) |
| `TransitInfoAPI/Models/Paginated.cs` | Paginated list response (TransitInfoAPI) |
| `GetThereShared/Common/OperationResult.cs` | API response wrapper (GetThereAPI only) |
| `GetThereShared/Contracts/*.cs` | Request/response DTOs |
| `GetThereAPI/Program.cs` | Service registration, startup |
| `GetThereAPI/Controllers/TicketingController.cs` | Ticket purchase, options, listing |
| `GetThereAPI/Managers/TicketingManager.cs` | Ticket business logic (wallet deduction, adapter dispatch) |
| `GetThereAPI/Sdk/ITicketingAdapter.cs` | Ticketing provider adapter contract |
| `GetThereAPI/Managers/WalletManager.cs` | Wallet balance, top-up, ensure |
| `GetThere/MauiProgram.cs` | MAUI DI setup, API base URL per platform |
| `GetThere/Platforms/Map/map.html` | Map bundle (MapLibre GL JS) |
| `TransitInfoAPI/Managers/ReconciliationManager.cs` | Station reconciliation logic |
| `TransitInfoAPI/Controllers/ReconciliationController.cs` | Reconciliation approve/reject/reassign endpoints |

## Database
- Connection string (dev): `Server=localhost;Database=GetThereDB;Trusted_Connection=True;TrustServerCertificate=True`
- Migrations (GetThereAPI): `dotnet ef migrations add <Name> --project GetThereAPI`
- Migrations (TransitInfoAPI): `dotnet ef migrations add <Name> --project TransitInfoAPI`
- Apply: `dotnet ef database update --project GetThereAPI`

## Running
- GetThereAPI: `cd GetThereAPI && dotnet run` → https://localhost:7230
- TransitInfoAPI: `cd TransitInfoAPI && dotnet run` → http://localhost:5000
- Android: `dotnet build -t:Run -f net10.0-android`
- Windows: `dotnet build -t:Run -f net10.0-windows10.0.19041.0`

## Adding new features
- New API endpoint (GetThereAPI): Controller → Manager → Mapper → Contract → MAUI Service
- New transit operator: insert row in `Operators` (core identity) + `TransitFeedConfigs` (GTFS feeds) — see `README.md` for the 3-concern operator model
- New ticketing provider: implement `ITicketingAdapter` → register in `AdapterRegistry`
- New bike provider: implement `IMobilityParser` → add case to `MobilityParserFactory` → insert DB row
- New GTFS-RT format: implement `IRealtimeParser` → add case to `RealtimeParserFactory`
- New MAUI page: create in `Pages/` → register route in Shell → DI auto-resolves constructor deps

## Notes
- JWT secret is in `appsettings.json` — move to env vars before production
- SSL validation bypassed in MAUI dev builds — remove before production
- Seed data includes mock payment keys — review `HasData` calls before production
- Do not manually edit `AppDbContextModelSnapshot.cs` — auto-generated by EF Core

## Off-limits areas
These areas must not be modified without explicit human instruction:

| Area | Why |
|------|-----|
| JWT auth pipeline (token creation/validation) | Touches security — could lock all users out |
| Wallet balance deduction logic | Financial impact — requires testing |
| Ticket status transitions | Affects user-visible ticket validity |
| EF Core migration generated files | Auto-generated — manual edits are overwritten |
| Seed data removal in production | Requires coordinated deployment plan |
