---
# Fill in the fields below to create a basic custom agent for your repository.
# The Copilot CLI can be used for local testing: https://gh.io/customagents/cli
# To make this agent available, merge this file into the default repository branch.
# For format details, see: https://gh.io/customagents/config

name: Refactor Agent
description: You are Senior Software Engineer
---

# My Agent


**General rules for every task:**

- Do not change public method signatures unless the task explicitly says to
- Do not move files to different folders unless the task explicitly says to
- Do not introduce new NuGet packages
- Do not change database migrations or entity classes
- After each task, the project must compile and existing behavior must be preserved
- If a task says "create a new class," create it in the same project and folder as the class it is extracted from unless otherwise stated

---

## Task 1 — Extract Purchase Logic from MockTicketController

**Files to read first:**

- `GetThereAPI/Controllers/MockTicketController.cs`
- `GetThereAPI/Managers/TicketManager.cs`
- `GetThereAPI/Data/AppDbContext.cs`
- `GetThereShared/Dtos/ShopDtos.cs`

**What to do:**
1. Create a new class called `MockTicketPurchaseService` in `GetThereAPI/Managers/`. This class should take `AppDbContext` as a constructor dependency.
2. Move the entire body of the `Purchase` action method from `MockTicketController` into a new public async method on `MockTicketPurchaseService` called `PurchaseAsync`. The method signature should accept the user ID as a `string`, the operator ID as an `int`, and the `MockTicketPurchaseRequest` body. It should return `OperationResult<MockTicketResultDto>`.
3. The static dictionaries `Catalogue`, `ValidMinutes`, and `DbTransitOperatorIds` should also move into `MockTicketPurchaseService` as private static fields. They currently live on the controller and belong with the logic that uses them.
4. Update `MockTicketController` to take `MockTicketPurchaseService` as a constructor dependency. The `Purchase` action should extract the user ID from the JWT claims, call `_purchaseService.PurchaseAsync(userId, operatorId, body)`, and return the result. The action body should be no more than ten lines.
5. The `GetOptions` action does not change. It can stay in the controller as-is since it only reads from the static catalogue, but since the catalogue has moved, update `GetOptions` to read from `MockTicketPurchaseService` by either making the catalogue accessible via a public method `GetOptions(int operatorId)` on the service, or by moving `GetOptions` logic into the service as well. Prefer moving it into the service to keep the controller clean.
6. Register `MockTicketPurchaseService` in `GetThereAPI/Program.cs` as a scoped service.

**Acceptance criteria:**

- `MockTicketController` has no static fields
- `MockTicketController` has no database transaction logic
- `MockTicketController.Purchase` is ten lines or fewer
- `MockTicketPurchaseService` contains all wallet deduction, ticket creation, and transaction recording logic
- The purchase endpoint behavior is identical to before
- The project compiles

---

## Task 2 — Remove AppDbContext from PaymentController

**Files to read first:**

- `GetThereAPI/Controllers/PaymentController.cs`
- `GetThereAPI/Managers/PaymentManager.cs`

**What to do:**
1. Add a new public method to `PaymentManager` called `GetActiveProvidersAsync`. This method should contain the query currently in `PaymentController.GetProviders()` — it queries `PaymentProviders` where `IsActive` is true, orders by `Id`, and projects to `PaymentProviderDto`. It should return `Task<List<PaymentProviderDto>>`.
2. Update `PaymentController` to remove its `AppDbContext` constructor dependency entirely. The `GetProviders` action should call `_paymentManager.GetActiveProvidersAsync()` instead of querying the context directly.

**Acceptance criteria:**

- `PaymentController` has no reference to `AppDbContext`
- `PaymentController` has no using statements for `Microsoft.EntityFrameworkCore`
- `PaymentManager.GetActiveProvidersAsync` exists and returns the same data as before
- The project compiles

---

## Task 3 — Remove AppDbContext from MapController

**Files to read first:**

- `GetThereAPI/Controllers/MapController.cs`
- `GetThereAPI/Managers/OperatorManager.cs`

**What to do:**
1. `MapController` performs a country name lookup in two places — once in `GetFeatures` and once in `GetBikeStations`. Both do the same thing: given a `countryId`, look up the country name from the `Countries` table.
2. Add a new private async method to `OperatorManager` called `GetCountryNameAsync` that takes a nullable `int countryId` and returns `Task<string?>`. It should query `_db.Countries` for the matching name, or return null if the ID has no value or no match is found.
3. Expose this as an internal or public method so `MapController` can call it, or alternatively expose it as part of the existing `GetAllStopsAsync` and bike station methods by moving the country resolution inside `OperatorManager` itself.
4. The cleaner approach is the second one: add an overload or update the existing `GetAllStopsAsync` in `OperatorManager` to accept a nullable `countryId` and resolve the country name internally. Do the same for whatever method `MapController` calls for bike stations — noting that bike stations currently come from `MobilityManager` directly in the controller.
5. Move the bike station retrieval out of `MapController` as well. Add a method to `OperatorManager` called `GetBikeStationsAsync` that accepts a nullable `countryId`, resolves the country name internally, and calls `_mobility.GetAllStations(countryName)`.
6. Update `MapController` to remove its `AppDbContext` dependency. Both actions should delegate entirely to `OperatorManager` for data retrieval.

**Acceptance criteria:**

- `MapController` has no reference to `AppDbContext`
- `MapController` has no using statements for `Microsoft.EntityFrameworkCore`
- Country name resolution happens inside `OperatorManager`, not in the controller
- `OperatorManager` has a `GetBikeStationsAsync(int? countryId)` method
- The project compiles

---

## Task 4 — Extract IBikeStationCache from MobilityManager

**Files to read first:**

- `GetThereAPI/Managers/MobilityManager.cs`
- `GetThereAPI/Managers/OperatorManager.cs`
- `GetThereAPI/Program.cs`

**What to do:**
1. Create a new interface file at `GetThereAPI/Managers/IBikeStationCache.cs`. The interface should declare exactly the public methods on `MobilityManager` that `OperatorManager` currently calls. Looking at the code these are: `GetAllStations()` with no parameters, `GetAllStations(string? countryName)` with a country name parameter, and `HasStationsInCountry(int providerId, string countryName)`. Declare all three on the interface.
2. Make `MobilityManager` implement `IBikeStationCache`. No logic changes — just add the interface to the class declaration.
3. Update `OperatorManager` to accept `IBikeStationCache` in its constructor instead of `MobilityManager`. Update all usages inside `OperatorManager` accordingly. No logic changes.
4. Update `GetThereAPI/Program.cs` to register `IBikeStationCache` pointing at the `MobilityManager` singleton. Since `MobilityManager` is already registered as a singleton and as a hosted service, add an additional registration that resolves `IBikeStationCache` by retrieving the existing `MobilityManager` singleton from the service provider. Do not register a second instance of `MobilityManager`.

**Acceptance criteria:**

- `IBikeStationCache` interface exists with three method declarations
- `MobilityManager` implements `IBikeStationCache`
- `OperatorManager` constructor takes `IBikeStationCache`, not `MobilityManager`
- Only one instance of `MobilityManager` exists at runtime
- The project compiles

---

## Task 5 — Extract IIconFileStore and Remove IWebHostEnvironment from OperatorManager

**Files to read first:**

- `GetThereAPI/Managers/OperatorManager.cs`
- `GetThereAPI/Controllers/OperatorController.cs`
- `GetThereAPI/Program.cs`

**What to do:**
1. Create a new interface file at `GetThereAPI/Infrastructure/IIconFileStore.cs`. The interface should declare one method: `bool Exists(string filename)`.
2. Create a new class in the same folder called `WebRootIconFileStore` that implements `IIconFileStore`. Its constructor should accept `IWebHostEnvironment`. The `Exists` method should combine the web root path with an images subfolder and the given filename, and return whether that file exists on disk. This is exactly the logic currently inside `OperatorManager.GetTransportTypesAsync`.
3. Update `OperatorManager` to accept `IIconFileStore` as a constructor dependency. Update `GetTransportTypesAsync` to remove the `IWebHostEnvironment` parameter entirely and use `_iconFileStore.Exists(t.IconFile)` instead.
4. Update `OperatorController` where it calls `GetTransportTypesAsync` — remove the `_env` argument since the method no longer takes it.
5. Remove `IWebHostEnvironment` from `OperatorController`'s constructor if it was only used for this call. Check the controller for any other usage before removing it.
6. Register `IIconFileStore` as a singleton in `Program.cs` using `WebRootIconFileStore`.

**Acceptance criteria:**

- `IIconFileStore` interface exists
- `WebRootIconFileStore` exists and implements it
- `OperatorManager.GetTransportTypesAsync` takes no parameters
- `OperatorManager` has no reference to `IWebHostEnvironment`
- `OperatorController` passes no environment argument to `GetTransportTypesAsync`
- The project compiles

---

## Task 6 — Split OperatorManager into Focused Classes

**Files to read first:**

- `GetThereAPI/Managers/OperatorManager.cs`
- `GetThereAPI/Controllers/OperatorController.cs`
- `GetThereAPI/Controllers/MapController.cs`
- `GetThereAPI/Program.cs`

**Note:** Complete Tasks 3, 4, and 5 before this task.

### What to do:
#### Part A — Create TicketableCatalogueService
- Create `GetThereAPI/Managers/TicketableCatalogueService.cs`. Move into it everything related to the ticketable operator catalogue: the `TicketableList` static field, the `MobilityProviderIds` static field, the `TicketableToDbTransitId` static field, and the entire `GetTicketableOperatorsAsync` method. This class should take `AppDbContext` and `IBikeStationCache` as constructor dependencies.

#### Part B — Create TransitDataService
- Create `GetThereAPI/Managers/TransitDataService.cs`. Move into it the stop and route retrieval methods: `GetAllStopsAsync`, `GetAllRoutesAsync`, `GetStopScheduleAsync`, `IsTransitHealthyAsync`, and the private `GetFeedPrefixesForCountryAsync` helper. This class should take `AppDbContext` and `TransitOrchestrator` as constructor dependencies.

#### Part C — Simplify OperatorManager
- After the above extractions, `OperatorManager` should retain only: `GetAllOperatorsAsync`, `GetTransportTypesAsync`, `GetOtpFeedOperatorsAsync`, and `GetBikeStationsAsync` (added in Task 3). Its constructor dependencies should reduce to `AppDbContext`, `IBikeStationCache`, and `IIconFileStore`.

#### Part D — Update controllers and DI
- Update `OperatorController` to take `TicketableCatalogueService` and `TransitDataService` as dependencies where needed, replacing the calls that previously went through `OperatorManager`.
- Update `MapController` similarly for any calls that moved to `TransitDataService`.
- Register `TicketableCatalogueService` and `TransitDataService` as scoped services in `Program.cs`.
- Remove `TransitOrchestrator` from `OperatorManager`'s constructor since it has moved to `TransitDataService`.

**Acceptance criteria:**

- `OperatorManager` no longer contains ticketable catalogue logic
- `OperatorManager` no longer contains stop/route/schedule retrieval logic
- `TicketableCatalogueService` exists and owns all ticketable catalogue logic
- `TransitDataService` exists and owns all OTP transit data retrieval logic
- All existing API endpoints return the same data as before
- The project compiles

---

## Task 7 — Replace Reflection-Based DI Registration in MauiProgram.cs

**Files to read first:**

- `GetThere/MauiProgram.cs`
- All files in `GetThere/Services/`

**What to do:**
1. Read all service classes in `GetThere/Services/` and make a list of every class that is currently being auto-registered by the reflection loop. The loop registers every class in the `GetThere.Services` namespace that is not `AuthService` and not `AuthenticatedHttpHandler`.
2. Replace the reflection loop with explicit individual registrations for each service. Each registration should use `AddHttpClient` with the `AuthenticatedHttpHandler` and the SSL bypass handler, exactly as the loop currently does. The behavior must be identical — same handler configuration, same lifetime, same base URL.
3. The page auto-registration loop (the second reflection loop that registers `ContentPage` subclasses) can remain as-is. That one is lower risk since pages do not carry the same "accidentally register wrong things" danger as services.
4. After the change, the services section of `MauiProgram.cs` should read as an explicit list of service registrations that anyone can scan in five seconds.

**Acceptance criteria:**

- No reflection-based service registration exists for the `GetThere.Services` namespace
- Every service that was previously auto-registered is now explicitly registered
- The application behavior is identical
- The project compiles

---

## Task 8 — Fix MobilityParserFactory to Use DI-Based Lookup

**Files to read first:**

- `GetThereAPI/Parsers/Mobility/MobilityParserFactory.cs`
- `GetThereAPI/Parsers/Mobility/IMobilityParser.cs`
- `GetThereAPI/Parsers/Mobility/NextbikeParser.cs`
- `GetThereAPI/Managers/MobilityManager.cs`
- `GetThereAPI/Program.cs`

**What to do:**
1. Update `MobilityParserFactory` to accept `IServiceProvider` as a constructor dependency instead of holding a static `NextbikeParser` instance.
2. Update the `GetParser` method to retrieve the parser using `IServiceProvider.GetKeyedService<IMobilityParser>(provider.FeedFormat)` with a fallback to `NextbikeParser` if no keyed registration is found for the given format. This preserves the existing behavior while enabling future parsers to be registered without modifying the factory.
3. Register `NextbikeParser` as a keyed singleton in `Program.cs` using `MobilityFeedFormat.NEXTBIKE_API` as the key. Also register it for `MobilityFeedFormat.GBFS`, `MobilityFeedFormat.BOLT_API`, and `MobilityFeedFormat.REST` as the current placeholders, so the behavior is identical to the current switch expression.
4. Register `MobilityParserFactory` as a singleton in `Program.cs`. Update `MobilityManager` to take `MobilityParserFactory` as a constructor dependency if it does not already.

**Acceptance criteria:**

- `MobilityParserFactory` has no switch expression
- Adding a new parser in the future requires only a new class and a new DI registration
- `NextbikeParser` is registered for all four feed formats in `Program.cs`
- The project compiles

---

## Task 9 — Standardize Error Handling in MAUI Services

**Files to read first:**

- All files in `GetThere/Services/`
- `GetThereShared/Common/OperationResult.cs`

**What to do:**
1. Audit every public method in every service class in `GetThere/Services/`. Identify all methods that currently return a nullable type such as `List<T>?` or any other nullable instead of `OperationResult<T>`.
2. For each such method, change the return type to `OperationResult<T>` where `T` is the unwrapped type. Update the method body to return `OperationResult<T>.Ok(data)` on success and `OperationResult<T>.Fail(message)` on failure. Wrap the HTTP call in a try/catch and return a failure result in the catch block rather than returning null or trace-logging and swallowing the exception.
3. After changing service signatures, find every call site in the MAUI pages and update them to use the new return type. Pages that previously null-checked the result should now check `result.Success` and use `result.Data`.
4. Services that already return `OperationResult<T>` correctly — such as `WalletService`, `AuthService`, and `PaymentService` — do not need to change. Only bring the inconsistent ones into line.

**Acceptance criteria:**

- No service method in `GetThere/Services/` returns a nullable collection or nullable DTO directly
- All service methods return `OperationResult<T>`
- All call sites in pages are updated to match
- The project compiles

---

## Task 10 — Clean Up MockTicketStore Comment

**Files to read first:**

- `GetThere/State/MockTicketStore.cs`
- `GetThereAPI/Controllers/MockTicketController.cs`

**What to do:**
The summary comment on `MockTicketStore` says the store works "without requiring network calls, and without needing the user to be logged in." The second part of this is inaccurate — the purchase endpoint has `[Authorize]` on it and does require the user to be logged in.

- Update the class summary comment to remove the inaccurate claim. The comment should accurately describe what `MockTicketStore` does: it is an in-memory store for mock tickets purchased during the current app session, providing access to recently purchased tickets without requiring repeated API calls. Remove the claim about not needing to be logged in.
- This is a documentation-only change. No logic changes.

**Acceptance criteria:**

- The comment no longer states that the user does not need to be logged in
- No logic is changed
- The project compiles

---

## Execution Order Summary

| Order | Task Name                                                      | Depends On        |
|-------|----------------------------------------------------------------|-------------------|
| 1     | Extract purchase logic from MockTicketController               | Nothing           |
| 2     | Remove AppDbContext from PaymentController                     | Nothing           |
| 3     | Remove AppDbContext from MapController                         | Nothing           |
| 4     | Extract IBikeStationCache                                      | Nothing           |
| 5     | Extract IIconFileStore                                         | Nothing           |
| 6     | Split OperatorManager into Focused Classes                     | Tasks 3, 4, 5     |
| 7     | Replace reflection-based DI registration                       | Nothing           |
| 8     | Fix MobilityParserFactory                                      | Nothing           |
| 9     | Standardize MAUI service error handling                        | Nothing           |
| 10    | Fix MockTicketStore comment                                    | Nothing           |

Tasks 1-5 and tasks 7-10 are all independent and can be done in any order or in parallel. Task 6 should be done after tasks 3, 4, and 5 are complete.
