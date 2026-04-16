# GetThereShared — Detailed Code Documentation

## 1. Purpose of this project
`GetThereShared` is the shared contract library used by both:
- `GetThere` (MAUI client),
- `GetThereAPI` (backend API).

It centralizes transport objects and common result wrappers to prevent contract drift.

---

## 2. Main contents

### DTOs (`GetThereShared/Dtos`)
Contains request/response objects grouped by domain:
- `AuthDtos`
- `CountryDto`
- `MapDtos` / `MapFeatureDtos`
- `OperatorDto` / `OtpOperatorFeedDtos`
- `ScheduleDtos`
- `ShopDtos`
- `TicketingDtos`
- `WalletDtos`
- `PaymentDtos`
- `BikeStationDto`

### Common wrappers (`GetThereShared/Common`)
- `OperationResult`
- `OperationResult<T>`

These are standard response envelopes across API endpoints and client parsing logic.

### Enums (`GetThereShared/Enums`)
Shared state definitions such as:
- `PaymentStatus`
- `TicketFormat`
- `TicketStatus`
- `WalletTransactionType`

---

## 3. Why this project is critical

Without a shared contract assembly, client and backend can silently diverge:
- property name mismatches,
- missing fields,
- enum interpretation differences,
- inconsistent error response handling.

`GetThereShared` prevents that by making compile-time contracts explicit.

---

## 4. How future shared code should be done

### Backward-compatible evolution
- Prefer additive changes (new optional fields) over breaking renames/removals.
- Keep old fields until both API and app are migrated.
- If a breaking change is unavoidable, version DTOs explicitly (`XxxV2Dto`).

### DTO design rules
- DTOs should be transport-focused, not EF entities.
- Keep them flat and serialization-friendly.
- Avoid embedding runtime services/logic in DTO classes.

### Enum governance
- Treat enum values as API contract.
- When adding enum values, ensure UI and backend both handle unknown/default cases safely.

### OperationResult consistency
- Continue using `OperationResult` wrappers for predictable client behavior.
- Keep `Success`, `Message`, and `Data` semantics stable.

---

## 5. Recommended future improvements
- Add XML docs for every DTO property that is externally consumed.
- Add contract test suite to verify API JSON matches DTO shape.
- Add nullable-reference audit to ensure DTO nullability precisely reflects real API behavior.
