# GetThereShared — Full Code Documentation

## 1) Project role in the solution
`GetThereShared` is the shared contract and primitive library that prevents API/client mismatch.

It is consumed by:
- `GetThereAPI` (producer of DTO payloads),
- `GetThere` (consumer/parser of DTO payloads).

---

## 2) Project structure
- `Common/`
  - operation result wrappers.
- `Dtos/`
  - all transport payload contracts by domain.
- `Enums/`
  - shared enum contracts used in DTOs/entities.

Project file: `GetThereShared/GetThereShared.csproj` targeting `net10.0`.

---

## 3) Core shared primitives

### OperationResult
File: `Common/OperationResult.cs`
- `Success` (bool)
- `Message` (string)
- static builders:
  - `Ok(...)`
  - `Fail(...)`

### OperationResult<T>
- inherits `OperationResult`
- adds `Data` payload
- standard success/failure creation helpers

These wrappers enforce predictable API response shape.

---

## 4) DTO catalogue (detailed)

### Auth DTOs (`AuthDtos.cs`)
- `LoginDto`: email/password request.
- `RegisterDto`: email/password/fullname request.
- `UserDto`: login response identity payload + optional JWT token.

### Country DTO (`CountryDto.cs`)
- Country selector payload (`Id`, `Name`).

### Operator DTOs (`OperatorDto.cs`)
- `OperatorDto`: public operator list item.
- `TransportTypeDto`: route type -> icon/color mapping for map rendering.

### Map DTOs (`MapDtos.cs`)
- `StopDto`: stop coordinates and dominant route type.
- `VehicleDto`: vehicle position metadata (structure exists for live vehicle map rendering).
- `RouteDto`: route metadata + optional polyline coordinates.

### Schedule DTOs (`ScheduleDtos.cs`)
- `StopScheduleDto`: stop-level grouped departures.
- `DepartureGroupDto`: route/headsign group.
- `DepartureDto`: single departure with delay/realtime fields.
- `TripDetailDto` + `TripStopDto`: trip detail structures.

### Unified map feature envelope (`MapFeatureDtos.cs`)
- `MapFeatureDto`: discriminator + coordinates + typed JSON payload.

### Wallet DTOs (`WalletDtos.cs`)
- `WalletDto`
- `WalletTransactionDto` (includes derived formatted amount helper).

### Payment DTOs (`PaymentDtos.cs`)
- `PaymentDto`
- `PaymentProviderDto`
- `TopUpDto`

### Ticket DTO (`TicketingDtos.cs`)
- `TicketDto`: purchased ticket metadata/payload.

### Shop DTOs (`ShopDtos.cs`)
- `TicketableOperatorDto`
- `MockTicketOptionDto`
- `MockTicketResultDto`
- `MockTicketPurchaseRequest`

### Mobility DTO (`BikeStationDto.cs`)
- Bike station payload with country-name for dynamic filtering.

### OTP feed DTO (`OtpOperatorFeedDtos.cs`)
- Operator feed metadata consumed by OpenTripPlannerAPI config loader.

---

## 5) Enum contracts
Location: `GetThereShared/Enums`
- `WalletTransactionType`
- `PaymentStatus`
- `TicketFormat`
- `TicketStatus`

These enums are contract-critical because:
- API serializes them,
- MAUI client interprets them,
- DB stores enum values as strings (in backend model layer).

---

## 6) Why this project is architecturally essential
Without `GetThereShared`, contract drift risk increases quickly:
- duplicated model definitions,
- subtle serialization naming mismatches,
- breaking changes missed until runtime,
- inconsistent enum handling across app/backend.

This project provides compile-time shared truth.

---

## 7) Rules for future shared-contract changes

### Rule A — additive-first evolution
Prefer adding optional properties over removing/renaming existing ones.

### Rule B — contract versioning for breaks
If break is unavoidable, add versioned DTO (`XxxV2Dto`) and migrate consumers explicitly.

### Rule C — avoid domain leakage
Keep DTOs transport-focused; do not leak EF/entity internals or service dependencies.

### Rule D — nullability clarity
Use nullable reference types intentionally to communicate optional vs required contract semantics.

### Rule E — wrapper consistency
API endpoints should keep using `OperationResult` wrappers so client handling remains uniform.

---

## 8) Recommended future improvements
- Add XML docs at property level for all externally consumed DTO properties.
- Add contract tests verifying backend JSON payloads deserialize into shared DTOs.
- Add schema change policy notes in docs to standardize compatibility process.
