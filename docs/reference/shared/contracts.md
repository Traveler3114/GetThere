# GetThereShared — Contract Reference

`GetThereShared` is the only assembly referenced by **both** the MAUI client and `GetThereAPI`. It
contains no behaviour beyond formatting and parsing helpers — it exists so that a change to a DTO
breaks compilation on both sides at once instead of failing silently over the wire.

- **Namespace roots:** `GetThereShared.Contracts`, `GetThereShared.Enums`, `GetThereShared.Common`
- **Referenced by:** `GetThere` (MAUI) and `GetThereAPI` — those two only.
- **Never referenced by `TransitInfoAPI`.** That service is independent: its contracts live in
  `TransitInfoAPI.Contracts`, and it must not take a dependency on this assembly, on `GetThereAPI`
  or on the client. The two systems no longer talk at all — `TransitInfoApiClient`, which re-mapped
  upstream shapes into the `Map*` types here, was removed on 2026-08-02. The client reads
  TransitInfoAPI's own shapes directly in the map page, which is JavaScript and pins no C# type, so a
  transit contract change still cannot break the client by construction.

  > A `ProjectReference` from `TransitInfoAPI` to `GetThereShared` did exist, used only for
  > `RoleDto`/`UserDto`. It was removed and those types now live in `TransitInfoAPI.Contracts`.
  > If it reappears, that is a regression, not a shortcut.

> **Serialization note.** Enums cross the wire as **integers** unless a converter says otherwise, so
> the ordinal values below are part of the contract. Inserting a member anywhere but the end is a
> breaking change for any client already in the field.

---

## Contents

| Area | Types |
|---|---|
| [Auth](#auth) | `RegisterRequest`, `LoginRequest`, `RefreshTokenRequest`, `LoginResponse`, `RefreshTokenResponse`, `UserResponse`, `UpdateProfileRequest` |
| [Tickets](#tickets) | `TicketOptionResponse`, `TicketResponse`, `PurchaseTicketRequest` |
| [Imported tickets](#imported-tickets) | `CreateImportedTicketRequest`, `ImportedTicketResponse`, `UpdateImportedTicketStatusRequest` |
| [Ticket import / extraction](#ticket-import--extraction) | `TicketExtractionResult`, `ExtractTicketTextRequest`, `TicketUploadResponse` |
| [Journeys](#journeys) | `CreateJourneyRequest`, `UpdateJourneyRequest`, `JourneyMembershipRequest`, `JourneyLegResponse`, `JourneyResponse`, `JourneySuggestionResponse` |
| [Wallet](#wallet) | `WalletResponse`, `WalletTransactionResponse`, `TopUpRequest` |
| [Map](#map) | `MapStationResponse`, `MapRouteResponse`, `MapMobilityStationResponse`, `MapVehicleResponse`, `MapDepartureResponse`, `MapOperatorResponse` |
| [Admin](#admin) | `UserListItem`, `AdminStats`, `PurchaseListItem`, `AdapterHealthItem`, `AuditLogEntry` |
| [Roles](#roles) | `RoleDto`, `UserDto` |
| [Settings & countries](#settings--countries) | `UserSettingsResponse`, `UpdateSettingsRequest`, `CountryResponse` |
| [Enums](#enums) | 9 enums |
| [Common](#common) | `OperationResult`, `PagedResult<T>`, `MoneyFormatter`, `SupportedCurrencies`, `HttpHelper`, `Base64Helper` |

---

## Auth

`Contracts/AuthContract.cs`

Requests are positional `record`s; responses are mutable classes with `[Required]` for OpenAPI
non-nullability.

### `RegisterRequest` (record)

| Param | Type | Validation |
|---|---|---|
| `Email` | `string` | `[Required]`, `[EmailAddress]` |
| `Password` | `string` | `[Required]`, `[MinLength(12)]` |
| `FullName` | `string` | `[Required]` |

The 12-character floor is enforced here **and** by ASP.NET Identity options in `Program.cs`; the
attribute is what produces the 400 before the request reaches Identity.

### `LoginRequest` (record)

| Param | Type | Validation |
|---|---|---|
| `Email` | `string` | `[Required]`, `[EmailAddress]` |
| `Password` | `string` | `[Required]` |

### `RefreshTokenRequest` (record)

| Param | Type | Validation |
|---|---|---|
| `RefreshToken` | `string` | `[Required]` |

### `LoginResponse`

| Property | Type | Notes |
|---|---|---|
| `User` | `UserResponse` | `[Required]`, `null!` default |
| `AccessToken` | `string` | `[Required]` — JWT |
| `RefreshToken` | `string` | `[Required]` — opaque, stored hashed server-side |

### `RefreshTokenResponse`

| Property | Type |
|---|---|
| `AccessToken` | `string` |
| `RefreshToken` | `string` |

Both `[Required]`. The refresh token is **rotated** — the value returned here replaces the one sent.

### `UserResponse`

| Property | Type | Notes |
|---|---|---|
| `Id` | `string` | Identity GUID as string |
| `Email` | `string` | `[Required]`, `[EmailAddress]` |
| `FullName` | `string?` | |

### `UpdateProfileRequest` (record)

| Property | Type | Validation |
|---|---|---|
| `FullName` | `string?` | — |
| `Email` | `string?` | `[EmailAddress]` |

Both optional: omitted properties leave the stored value alone.

---

## Tickets

`Contracts/TicketContract.cs`

### `TicketOptionResponse`

A purchasable product exposed by one ticketing adapter.

| Property | Type | Default | Notes |
|---|---|---|---|
| `Id` | `int` | | |
| `AdapterId` | `int` | | FK to the adapter |
| `AdapterName` | `string` | `""` | Display name |
| `AdapterType` | `string` | `""` | Type slug, e.g. `hzpp.v1`. Printed under the QR on the ticket screen so a ticket can say which integration issued it. |
| `ExternalProductId` | `string` | `""` | The operator's own product id |
| `Name` | `string` | `""` | |
| `Description` | `string?` | | |
| `Price` | `decimal` | | |
| `Currency` | `string` | `"EUR"` | |
| `TicketFormat` | `TicketFormat` | | What the issued ticket will be |
| `DurationMinutes` | `int?` | | Null for options with no fixed validity window |

### `TicketResponse`

| Property | Type | Default | Notes |
|---|---|---|---|
| `Id` | `int` | | |
| `PurchaseId` | `int` | | |
| `ExternalTicketId` | `string?` | | Operator-side id, null until the adapter returns one |
| `Format` | `TicketFormat` | | |
| `Data` | `string` | `""` | Payload — QR/barcode content, PDF bytes as base64, or a reference code, per `Format` |
| `ValidFrom` | `DateTime?` | | |
| `ValidTo` | `DateTime?` | | |
| `Status` | `TicketStatus` | | |
| `Option` | `TicketOptionResponse` | `null!` | Always populated on read |

### `PurchaseTicketRequest` (record)

| Property | Type |
|---|---|
| `AdapterId` | `int` |
| `OptionId` | `int` |

No amount is sent — the server prices the purchase from the option, so a client cannot name its own
price.

---

## Imported tickets

`Contracts/ImportedTicketContract.cs`

Tickets the user already holds, brought in from a file or typed by hand, as opposed to bought
through an adapter.

### `CreateImportedTicketRequest`

| Property | Type | Validation | Notes |
|---|---|---|---|
| `ClientId` | `Guid?` | | Minted by the device at creation. The idempotency key for the offline import queue — a replay returns the original ticket instead of a second copy. Omit for a ticket created directly against the API |
| `OperatorGlobalId` | `string?` | `[MaxLength(128)]` | Onestop-style id from TransitInfoAPI |
| `OperatorNameSnapshot` | `string?` | `[MaxLength(200)]` | Denormalised so the ticket still reads correctly if the operator is renamed or removed |
| `Source` | `ImportSource?` | `[Required]` | Nullable so a missing value fails validation instead of defaulting to `Manual` (ordinal 0) |
| `TicketName` | `string?` | `[MaxLength(200)]` | |
| `RouteDescription` | `string?` | `[MaxLength(500)]` | Free text |
| `OriginName` | `string?` | `[MaxLength(200)]` | Structured endpoint — journey chaining uses this |
| `DestinationName` | `string?` | `[MaxLength(200)]` | |
| `Price` | `decimal?` | `[Range(0, double.MaxValue)]` | |
| `Currency` | `string?` | `[MaxLength(3)]` | ISO-4217 |
| `ValidFrom` | `DateTime?` | | |
| `ValidTo` | `DateTime?` | | |
| `RawPayload` | `string?` | `[MaxLength(8000)]` | Decoded barcode content |
| `PayloadFormat` | `TicketFormat?` | | |
| `SourceFileBlobKey` | `string?` | `[MaxLength(128)]` | See below |
| `AllowDuplicate` | `bool` | | See below |

**`SourceFileBlobKey`** — a key returned by `POST /importedtickets/upload`. It is *not* a path and
*not* free-form: the server resolves it against the caller's own unconsumed uploads and rejects
anything else, so a client cannot name a file it did not upload. **Required whenever `Source` is
anything other than `ImportSource.Manual`.**

**`AllowDuplicate`** — import anyway when the server flagged this as a likely duplicate. Intended to
be set only after the user has been shown the clash: two passengers on the same route on the same
day are a legitimate pair of tickets, and a hard 409 left them no way through.

### `ImportedTicketResponse`

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | |
| `ClientId` | `Guid?` | Set when the ticket was created on a device; how that device recognises its own |
| `OperatorGlobalId` | `string?` | |
| `OperatorNameSnapshot` | `string?` | |
| `Source` | `ImportSource` | |
| `Status` | `ImportedTicketStatus` | |
| `Verification` | `VerificationStatus` | |
| `TicketName` | `string?` | |
| `RouteDescription` | `string?` | |
| `OriginName` | `string?` | |
| `DestinationName` | `string?` | |
| `Price` | `decimal?` | |
| `Currency` | `string?` | |
| `ValidFrom` | `DateTime?` | |
| `ValidTo` | `DateTime?` | |
| `RawPayload` | `string?` | |
| `PayloadFormat` | `TicketFormat?` | |
| `SourceFileBlobKey` | `string?` | Present when a file is attached |
| `SourceFileContentType` | `string?` | Sniffed, not the caller-supplied header |
| `JourneyId` | `int?` | Null when the ticket is not in a journey |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime` | |

### `UpdateImportedTicketStatusRequest`

| Property | Type | Validation |
|---|---|---|
| `Status` | `ImportedTicketStatus` | `[Required]` |

---

## Ticket import / extraction

`Contracts/TicketImportContract.cs`

### `TicketExtractionResult`

Everything an extractor could read off an uploaded file. **Every field is a candidate, not a
commitment** — the user confirms or corrects them in the import form before a ticket exists.

| Property | Type | Notes |
|---|---|---|
| `TicketName` | `string?` | |
| `RouteDescription` | `string?` | Free text |
| `OriginName` | `string?` | Structured endpoint, where the format carries one — a wallet pass always does, a PDF sometimes. Journey grouping chains on these; free-text `RouteDescription` cannot support it. |
| `DestinationName` | `string?` | |
| `OperatorNameSnapshot` | `string?` | |
| `Price` | `decimal?` | |
| `Currency` | `string?` | |
| `ValidFrom` | `DateTime?` | |
| `ValidTo` | `DateTime?` | |
| `RawPayload` | `string?` | Decoded barcode payload, when the file carried one |
| `PayloadFormat` | `TicketFormat?` | |
| `DetectedFields` | `List<string>` | Names of the fields above read **directly** from the file rather than inferred, so the UI can distinguish "this is what your ticket says" from "this is our best guess" |
| `Warning` | `string?` | Set when the file was readable but yielded nothing worth prefilling |

### `ExtractTicketTextRequest`

| Property | Type | Validation |
|---|---|---|
| `Text` | `string` | `[Required]`, `[MaxLength(20000)]` |

Pasted confirmation text to scrape for ticket fields. (Attributes are written fully-qualified in
source rather than via a `using`.)

### `TicketUploadResponse`

Result of `POST /importedtickets/upload`.

| Property | Type | Notes |
|---|---|---|
| `BlobKey` | `string` | Server-minted handle to the stored file. Pass back as `CreateImportedTicketRequest.SourceFileBlobKey`. **Single-use and scoped to the uploading user.** |
| `FileType` | `TicketFileType` | Sniffed from bytes |
| `ContentType` | `string` | |
| `SizeBytes` | `long` | |
| `Extraction` | `TicketExtractionResult` | The suggested ticket, for the user to confirm |

---

## Journeys

`Contracts/JourneyContract.cs`

A journey groups tickets — imported and purchased alike — into one trip.

### `CreateJourneyRequest`

| Property | Type | Validation | Notes |
|---|---|---|---|
| `Name` | `string` | `[Required]`, `[MaxLength(200)]` | |
| `Notes` | `string?` | `[MaxLength(2000)]` | |
| `ImportedTicketIds` | `List<int>` | | Placed in the journey immediately, so accepting a suggestion is one request rather than a create plus N adds |
| `TicketIds` | `List<int>` | | Purchased tickets, same |

### `UpdateJourneyRequest`

| Property | Type | Validation | Notes |
|---|---|---|---|
| `Name` | `string?` | `[MaxLength(200)]` | |
| `Notes` | `string?` | `[MaxLength(2000)]` | |
| `Status` | `JourneyStatus?` | | **Only `Cancelled` is settable by hand**; the rest roll forward from the legs |

### `JourneyMembershipRequest`

| Property | Type |
|---|---|
| `ImportedTicketIds` | `List<int>` |
| `TicketIds` | `List<int>` |

Used by both the add and remove endpoints.

### `JourneyLegResponse`

One leg, flattened across the imported and purchased ticket tables.

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | |
| `IsImported` | `bool` | True when this leg is an imported ticket, false when purchased — **the two share an id space only by accident**, so `Id` alone does not identify a leg |
| `TicketName` | `string?` | |
| `RouteDescription` | `string?` | |
| `OriginName` | `string?` | |
| `DestinationName` | `string?` | |
| `OperatorNameSnapshot` | `string?` | |
| `ValidFrom` | `DateTime?` | |
| `ValidTo` | `DateTime?` | |
| `Status` | `string` | Stringified — the two source tables use different status enums |
| `Price` | `decimal?` | |
| `Currency` | `string?` | |

### `JourneyResponse`

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | |
| `Name` | `string` | |
| `Notes` | `string?` | |
| `Status` | `JourneyStatus` | |
| `StartsAt` | `DateTime?` | Derived — earliest leg start |
| `EndsAt` | `DateTime?` | Derived — latest leg end |
| `LegCount` | `int` | |
| `Legs` | `List<JourneyLegResponse>` | **Populated on get-by-id, left empty on list responses** so the list stays cheap |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime` | |

### `JourneySuggestionResponse`

A proposed grouping the user can accept or ignore. **Never applied automatically** — a wrong guess
would silently reshuffle someone's wallet.

| Property | Type | Notes |
|---|---|---|
| `SuggestedName` | `string` | Derived from the first and last stop where those are known |
| `Reason` | `string` | Why these tickets were grouped, in words the UI can show directly |
| `Legs` | `List<JourneyLegResponse>` | |
| `ImportedTicketIds` | `List<int>` | Feed straight into `CreateJourneyRequest` |
| `TicketIds` | `List<int>` | |

---

## Wallet

`Contracts/WalletContract.cs`

### `WalletResponse`

| Property | Type | Default | Notes |
|---|---|---|---|
| `Balance` | `decimal` | | |
| `Currency` | `string` | `SupportedCurrencies.Default` (`"EUR"`) | |
| `RecentTransactions` | `List<WalletTransactionResponse>` | `[]` | |
| `FormattedBalance` | `string` (get-only) | | `[JsonIgnore]` — computed client-side via `MoneyFormatter.Format` |

### `WalletTransactionResponse`

| Property | Type | Default | Notes |
|---|---|---|---|
| `Id` | `int` | | |
| `Amount` | `decimal` | | |
| `Type` | `WalletTransactionType` | | |
| `Description` | `string?` | | |
| `CreatedAt` | `DateTime` | | |
| `Currency` | `string` | `"EUR"` | Set from the owning wallet; falls back to the default so rows stored before the field existed still render |
| `FormattedAmount` | `string` (get-only) | | `[JsonIgnore]` |

### `TopUpRequest` (record)

| Property | Type | Validation |
|---|---|---|
| `Amount` | `decimal` | `[Range(0.01, 1000.0)]`, message *"Amount must be between 0.01 and 1000."* |
| `PaymentMethod` | `string` | `[Required]`, `[StringLength(50, MinimumLength = 2)]` |

---

## Map

`Contracts/MapContract.cs`

Client-facing shapes for map data. `GetThereAPI` proxies TransitInfoAPI and re-maps into these, so
the client never holds a TransitInfoAPI type or credential.

### `MapStationResponse`

| Property | Type |
|---|---|
| `Id` | `int` |
| `OnestopId` | `string` |
| `Name` | `string` |
| `Latitude` | `double` |
| `Longitude` | `double` |
| `StationType` | `string?` |

### `MapRouteResponse`

| Property | Type |
|---|---|
| `Id` | `int` |
| `OnestopId` | `string` |
| `Name` | `string` |
| `RouteType` | `string?` |
| `OperatorName` | `string` |

### `MapMobilityStationResponse`

| Property | Type | Notes |
|---|---|---|
| `StationId` | `string` | GBFS station id (string, unlike transit stations) |
| `Name` | `string` | |
| `Latitude` | `double` | |
| `Longitude` | `double` | |
| `AvailableVehicles` | `int` | |
| `Capacity` | `int` | |
| `ProviderName` | `string` | |

### `MapVehicleResponse`

| Property | Type | Notes |
|---|---|---|
| `VehicleId` | `string` | |
| `RouteId` | `string?` | |
| `TripId` | `string?` | |
| `RouteShortName` | `string?` | |
| `IsRealtime` | `bool` | False for a schedule-interpolated position |
| `BlockId` | `string?` | |
| `Latitude` | `double` | |
| `Longitude` | `double` | |
| `Bearing` | `double?` | Degrees |
| `LastUpdated` | `DateTime?` | |

### `MapDepartureResponse`

| Property | Type |
|---|---|
| `TripId` | `string` |
| `RouteName` | `string` |
| `Headsign` | `string` |
| `ScheduledDeparture` | `DateTime?` |
| `EstimatedDeparture` | `DateTime?` |
| `DelaySeconds` | `int?` |

### `MapOperatorResponse`

| Property | Type | Notes |
|---|---|---|
| `GlobalId` | `string` | |
| `Name` | `string` | |
| `OperatorType` | `string` | |
| `HasTicketing` | `bool` | True when a `TicketingAdapter` in GetThereAPI is bound to this operator — this is the join between the two systems |

---

## Admin

`Contracts/AdminContract.cs`

### `UserListItem`

| Property | Type |
|---|---|
| `Id` | `string` |
| `Email` | `string` |
| `FullName` | `string?` |
| `CreatedAt` | `DateTime` |
| `LastLogin` | `DateTime?` |

### `AdminStats`

Aggregates shown on the admin overview KPI row.

| Property | Type | Notes |
|---|---|---|
| `Currency` | `string` | `"EUR"` default — all monetary fields below are in this currency |
| `TicketsSold` | `int` | |
| `TicketsSoldChangePercent` | `double` | |
| `TicketsSoldDaily` | `List<int>` | Purchase counts per day, **oldest first, ending with the current day** |
| `GrossVolume` | `decimal` | |
| `GrossVolumeChangePercent` | `double` | |
| `AverageBasket` | `decimal` | |
| `Refunds` | `int` | |
| `WalletFloat` | `decimal` | Total held in user wallets |
| `TopUps` | `decimal` | |
| `Spend` | `decimal` | |
| `PurchaseSuccessRate` | `double` | |
| `PurchaseSuccessRateChangePercent` | `double` | |
| `PendingPurchases` | `int` | |
| `OldestPendingPurchaseAt` | `DateTime?` | Stuck-purchase signal |
| `TotalUsers` | `int` | |
| `TotalTickets` | `int` | |
| `AdaptersDegraded` | `int` | |
| `AdaptersWithoutOptions` | `int` | |

### `PurchaseListItem`

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | |
| `TicketId` | `int?` | Null when the purchase never produced a ticket |
| `ExternalTicketId` | `string?` | |
| `UserEmail` | `string?` | |
| `OperatorName` | `string` | |
| `AdapterType` | `string` | |
| `OptionName` | `string` | |
| `Amount` | `decimal` | |
| `Currency` | `string` | `"EUR"` default |
| `PaymentStatus` | `string` | Stringified `PaymentStatus` |
| `TicketStatus` | `string?` | Stringified `TicketStatus`, null with no ticket |
| `PurchasedAt` | `DateTime` | |
| `FailureReason` | `string?` | |

### `AdapterHealthItem`

Health and configuration of one registered ticketing adapter.

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | |
| `Name` | `string` | |
| `AdapterType` | `string` | |
| `TransitInfoGlobalId` | `string` | The operator this adapter sells for |
| `BaseUrl` | `string` | |
| `IsActive` | `bool` | |
| `HasApiKey` | `bool` | **Boolean, never the key itself** |
| `IsRegistered` | `bool` | True when an SDK implementation is registered for `AdapterType` — a row can exist in the DB with no code behind it |
| `RequiredInputs` | `List<string>` | |
| `TicketOptions` | `int` | |
| `Purchases` | `int` | |
| `Failures` | `int` | |
| `Pending` | `int` | |
| `Volume` | `decimal` | |
| `LastPurchaseAt` | `DateTime?` | |
| `Status` | `string` | One of: `Ok`, `Degraded`, `Failing`, `Unregistered`, `Disabled`, `Idle`. Default `"Idle"` |

### `AuditLogEntry`

| Property | Type |
|---|---|
| `Id` | `int` |
| `UserId` | `string?` |
| `UserEmail` | `string?` |
| `Action` | `string` |
| `EntityType` | `string` |
| `EntityId` | `string` |
| `OldValues` | `string?` |
| `NewValues` | `string?` |
| `CreatedAt` | `DateTime` |

`OldValues` / `NewValues` are JSON blobs stored as text.

---

## Roles

`Contracts/RoleContract.cs`

### `RoleDto`

| Property | Type |
|---|---|
| `Name` | `string` |
| `Permissions` | `List<string>` |

Permission strings come from `GetThereAPI.Common.PermissionKeys`.

### `UserDto`

| Property | Type |
|---|---|
| `Id` | `string` |
| `Email` | `string` |
| `FullName` | `string` |
| `Roles` | `List<string>` |
| `CreatedAt` | `DateTime` |
| `LastLogin` | `DateTime?` |
| `IsActive` | `bool` |

> `UserDto`, `UserListItem`, and `UserResponse` overlap. They are deliberately separate: `UserResponse`
> is what a user sees about themselves, `UserListItem` is the cheap admin list row, and `UserDto` is
> the full admin view including roles and active state.

---

## Settings & countries

`Contracts/UserContract.cs`, `Contracts/CountryContract.cs`

### `UserSettingsResponse`

| Property | Type |
|---|---|
| `Theme` | `string?` |
| `Language` | `string?` |
| `NotificationsEnabled` | `bool` |
| `MapStyle` | `string?` |

### `UpdateSettingsRequest` (record)

| Property | Type | Notes |
|---|---|---|
| `Theme` | `string?` | |
| `Language` | `string?` | |
| `NotificationsEnabled` | `bool?` | **Nullable here** but not in the response — null means "leave unchanged" rather than "set false" |
| `MapStyle` | `string?` | |

### `CountryResponse`

| Property | Type |
|---|---|
| `Id` | `int` |
| `Name` | `string` |
| `Code` | `string?` |

---

## Enums

`Enums/*.cs`. Ordinal values are explicit below because they are the wire format.

### `ImportSource`

How a ticket got into the wallet.

| Value | Ordinal | Notes |
|---|---|---|
| `Manual` | 0 | Typed by hand — the only value that needs no uploaded file |
| `Photo` | 1 | |
| `Pdf` | 2 | |
| `QrScan` | 3 | |
| `PkPass` | 4 | An Apple Wallet pass |
| `Calendar` | 5 | An iCalendar booking confirmation |
| `Text` | 6 | Pasted confirmation text, scraped for fields |

Everything other than `Manual` requires an uploaded file, so those values are only accepted alongside
a blob key from the upload endpoint.

### `ImportedTicketStatus`

| Value | Ordinal |
|---|---|
| `Active` | 0 |
| `Used` | 1 |
| `Expired` | 2 |
| `Cancelled` | 3 |

### `TicketStatus`

| Value | Ordinal |
|---|---|
| `Active` | 0 |
| `Used` | 1 |
| `Expired` | 2 |
| `Cancelled` | 3 |
| `Refunded` | 4 |

Same first four members as `ImportedTicketStatus`, plus `Refunded` — a purchased ticket can be
refunded, an imported one cannot. They are **not** interchangeable types; `JourneyLegResponse.Status`
is a `string` precisely to avoid conflating them.

### `JourneyStatus`

Where a journey sits relative to now. Rolled forward from its member tickets' dates **by the expiry
worker rather than set by hand**, so it cannot drift from the legs it describes.

| Value | Ordinal | Meaning |
|---|---|---|
| `Planned` | 0 | Every leg is still ahead |
| `Active` | 1 | Under way — first leg started, last not finished |
| `Completed` | 2 | The last leg has passed |
| `Cancelled` | 3 | Abandoned by the user. Members are **released** rather than cancelled with it |

### `TicketFormat`

| Value | Ordinal |
|---|---|
| `QR` | 0 |
| `Barcode` | 1 |
| `PDF` | 2 |
| `NFC` | 3 |
| `Reference` | 4 |

### `TicketFileType`

A file format the ticket importer accepts. **Determined by sniffing the bytes, never by trusting the
uploaded `Content-Type` or file extension** — both are caller-supplied.

| Value | Ordinal | Notes |
|---|---|---|
| `Jpeg` | 0 | |
| `Png` | 1 | |
| `Webp` | 2 | |
| `Heic` | 3 | |
| `Pdf` | 4 | |
| `PkPass` | 5 | A ZIP whose `pass.json` carries the ticket fields |
| `ICalendar` | 6 | Typically a booking confirmation invite |

### `PaymentStatus`

| Value | Ordinal |
|---|---|
| `Pending` | 0 |
| `Completed` | 1 |
| `Failed` | 2 |
| `Refunded` | 3 |

### `VerificationStatus`

| Value | Ordinal |
|---|---|
| `Unverified` | 0 |
| `Verified` | 1 |
| `Suspicious` | 2 |

### `WalletTransactionType`

| Value | Ordinal |
|---|---|
| `Deposit` | 0 |
| `Withdrawal` | 1 |
| `TicketPurchase` | 2 |
| `Refund` | 3 |

---

## Common

`Common/*.cs`

### `OperationResult` / `OperationResult<T>`

Non-throwing result envelope.

```csharp
public class OperationResult
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = "";
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }
}
```

| Factory | Result |
|---|---|
| `OperationResult.Ok(string message = "")` | `Success = true` |
| `OperationResult.Fail(string message)` | `Success = false`, no code |
| `OperationResult.Fail(string code, string message)` | `Success = false`, with code |
| `OperationResult<T>.Ok(T data, string message = "")` | `Success = true`, `Data` set |
| `OperationResult<T>.Fail(string message)` | `new` shadow of the base |
| `OperationResult<T>.Fail(string code, string message)` | `new` shadow of the base |

Both have a public parameterless constructor for deserialization plus a `protected` `[JsonConstructor]`
overload. `Code` is the stable, machine-readable discriminator; `Message` is human-facing and gets
run through the client's `ApiMessageMapper` for localization.

### `PagedResult<T>` (record)

| Property | Type |
|---|---|
| `Data` | `List<T>` (init) |
| `Total` | `int` (init) |
| `Page` | `int` (init) |
| `PerPage` | `int` (init) |
| `TotalPages` | `int` (init) |
| `HasNextPage` | `bool` (computed) — `Page < TotalPages` |
| `HasPreviousPage` | `bool` (computed) — `Page > 1` |

Two constructors: the `[JsonConstructor]` five-arg form, and a four-arg convenience form that
computes `TotalPages` as `perPage < 1 ? 1 : ceil(total / perPage)` — the guard avoids a divide-by-zero
on a malformed page size.

### `MoneyFormatter`

One place that turns an amount plus a currency code into display text.

> Money was previously formatted two different ways, both wrong: `WalletTransactionResponse`
> hardcoded a euro sign and ignored the currency entirely, and the MAUI balance was formatted with a
> fixed `hr-HR` culture regardless of the user's language or the wallet's currency.

| Member | Signature | Behaviour |
|---|---|---|
| `SymbolFor` | `(string? currency) → string` | Symbol lookup; falls back to `"{code} "`, or the default currency plus a space when null |
| `Format` | `(decimal amount, string? currency, CultureInfo? culture = null) → string` | `"{symbol}{amount:N2}"`. Grouping and decimal separator follow `culture`, defaulting to `CultureInfo.CurrentCulture` |
| `FormatSigned` | `(decimal amount, string? currency, CultureInfo? culture = null) → string` | Explicit `+`/`-` prefix on the absolute value, for ledger rows |

Symbol table (case-insensitive): `EUR → €`, `USD → $`, `GBP → £`, `CHF → "CHF "`, `HRK → "kn "`.

### `SupportedCurrencies`

| Member | Value | Meaning |
|---|---|---|
| `All` | `["EUR","USD","GBP","CHF"]` | Currencies a new amount may be written in |
| `Legacy` | `["HRK"]` | Readable but no longer writable |
| `Selectable` | `["EUR","USD","GBP","CHF"]` | What the currency picker offers |
| `Default` | `"EUR"` | |
| `IsKnown(string?)` | `bool` | True for anything in `All` **or** `Legacy`, case-insensitive |

Validation and the picker are driven from the same list, so the API cannot accept something the UI is
unable to produce.

> **On HRK.** Retired in January 2023 when Croatia adopted the euro. Rows predating that keep
> rendering through `MoneyFormatter` rather than being rewritten, because there is no conversion rate
> stored and silently restating what someone recorded paying would be worse than showing it as paid.

### `HttpHelper`

| Member | Signature |
|---|---|
| `TryReadProblemAsync` | `(HttpResponseMessage, CancellationToken = default) → Task<string?>` |

Reads the `title` field out of an RFC 9457 problem response, or null when the body is not problem
JSON. Callers use the result to show a server-supplied message, so an unparseable body is an expected
outcome rather than an error — but the catch is narrowed to `JsonException` and
`NotSupportedException` so a cancellation or a broken connection still propagates.

### `Base64Helper`

| Member | Signature |
|---|---|
| `PadBase64` | `(string) → string` |

Converts base64url to standard base64 (`-` → `+`, `_` → `/`) and restores `=` padding. Used on JWT
segments and on barcode payloads that arrive URL-safe.
