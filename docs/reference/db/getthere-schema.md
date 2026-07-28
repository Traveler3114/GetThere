# GetThereDb — Schema Reference

SQL Server, EF Core 10, code-first. Context: `GetThereAPI.Data.AppDbContext`, deriving from
`IdentityDbContext<AppUser>`.

Migrations are **not** applied automatically at startup — unlike TransitInfoAPI. Deploying a schema
change means running `dotnet ef database update` explicitly. See
[../../guides/ef-database-commands.md](../../guides/ef-database-commands.md).

---

## Two conventions applied to the entire model

`OnModelCreating` loops over every entity type before any per-entity configuration:

### Every enum is stored as a string

```csharp
if (underlying.IsEnum)
    property.SetValueConverter(new EnumToStringConverter<…>());
```

The database holds `'Active'`, `'Completed'`, `'QR'` rather than `0`, `1`, `2`. Readable in ad-hoc
queries, and — more importantly — **reordering an enum member cannot silently reinterpret existing
rows**.

This has a load-bearing consequence: the `ImportedTickets` unique index filters on
`[Status] = 'Active'`. Switching to integer storage would silently break that index.

### Every foreign key is `Restrict`

```csharp
foreach (var fk in entityType.GetForeignKeys())
    fk.DeleteBehavior = DeleteBehavior.Restrict;
```

Nothing cascades by default, so no delete can silently take rows with it. Three FKs deliberately
override this **after** the loop:

| FK | Behaviour | Why |
|---|---|---|
| `AuditLogs.UserId` | `SetNull` | The audit trail must outlive the user it describes |
| `Purchases.WalletTransactionId` | `SetNull` | A purchase record survives its ledger row |
| `Journeys` → both ticket tables | `SetNull` | Deleting a journey **releases** its tickets |

The journey one is the most important, and the code says why: *the tickets are the valuable thing, the
grouping is not.*

---

## Tables

### Identity (`AspNet*`)

Standard ASP.NET Identity tables. `AspNetUsers` is extended by `AppUser`:

| Column | Type | Notes |
|---|---|---|
| `Id` | `nvarchar(450)` | PK, GUID string |
| `Email`, `UserName`, `PasswordHash`, … | | Identity defaults |
| `FullName` | `nvarchar(max)` | Added |
| `CreatedAt` | `datetime2` | Added |
| `LastLogin` | `datetime2` NULL | Added |

Permissions live in `AspNetRoleClaims` with `ClaimType = 'permission'` — this is what makes roles
editable at runtime rather than compiled in.

Identity's own settings (from `Program.cs`): password ≥12 chars with digit, uppercase and
non-alphanumeric; unique email required; lockout after 5 failures for 15 minutes.

### `RefreshTokens`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK identity |
| `Token` | `nvarchar(128)` | **Base64 SHA-256 hash**, never the token. **Unique index** |
| `UserId` | `nvarchar(450)` | FK → `AspNetUsers` |
| `ExpiresAt`, `CreatedAt` | `datetime2` | |
| `RevokedAt` | `datetime2` NULL | |
| `ReplacedByToken` | `nvarchar(128)` NULL | Set on rotation — drives reuse detection |
| `DeviceInfo` | `nvarchar(256)` NULL | User-Agent at issue |
| `IpAddress` | `nvarchar(64)` NULL | Enforced on refresh |

> **The `HasMaxLength(128)` is not cosmetic.** The column was originally unbounded, making it
> `nvarchar(max)` — which SQL Server **cannot index**. The declared unique index was silently never
> created and every refresh table-scanned. Fixed by `HardenRefreshTokenIndex`.

`IsExpired`, `IsRevoked` and `IsActive` are **computed in C# and unmapped**. EF cannot translate them,
so query predicates must be spelled out:

```csharp
.Where(rt => rt.RevokedAt == null && rt.ExpiresAt > now)   // not rt.IsActive
```

Using the property in a `Where` throws at runtime, and `AuthManager` carries a comment about it.

### `Wallets`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `UserId` | `nvarchar(450)` | FK, **unique** — one wallet per user |
| `Balance` | `decimal(18,2)` | |
| `Currency` | `nvarchar(max)` | Default `'EUR'` |
| `CreatedAt`, `UpdatedAt` | `datetime2` | |

Wallets are **not** created at registration; `POST /wallet/ensure` creates one on demand. The unique
index on `UserId` is what makes that safely idempotent under concurrency.

`decimal(18,2)` is why `TopUpAsync` rejects amounts with more than two decimal places — otherwise they
would round silently.

### `WalletTransactions`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `WalletId` | `int` | FK → `Wallets` |
| `Amount` | `decimal(18,2)` | **Signed** — negative for purchases |
| `BalanceBefore` | `decimal(18,2)` | |
| `BalanceAfter` | `decimal(18,2)` | |
| `Type` | `nvarchar` | `Deposit`, `Withdrawal`, `TicketPurchase`, `Refund` |
| `Description` | `nvarchar(max)` NULL | |
| `ReferenceId` | `nvarchar(max)` NULL | Purchase id on refunds |
| `CreatedAt` | `datetime2` | |

An append-only ledger. Storing `BalanceBefore`/`BalanceAfter` alongside the delta makes the history
independently auditable — a gap between one row's `BalanceAfter` and the next row's `BalanceBefore` is
a detectable inconsistency, which a bare delta column could not reveal.

Refunds are written as compensating rows, never by deleting the debit.

### `TicketingAdapters`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `TransitInfoGlobalId` | `nvarchar(max)` | **Indexed.** Soft reference to a TransitInfoAPI operator |
| `Name`, `AdapterType`, `BaseUrl` | `nvarchar(max)` | `AdapterType` keys the code registry |
| `ApiKeyEncrypted` | `nvarchar(500)` NULL | Never exposed; the API returns only `HasApiKey` |
| `IsActive` | `bit` | |
| `CreatedAt` | `datetime2` | |

`TransitInfoGlobalId` is a **string, not a foreign key** — it points into a different database, and
that looseness is deliberate so the two systems can move independently.

### `TicketOptions`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `TicketingAdapterId` | `int` | FK |
| `ExternalProductId` | `nvarchar(max)` | **Indexed** |
| `Name`, `Description` | `nvarchar(max)` | |
| `Price` | `decimal(18,2)` | |
| `Currency` | `nvarchar(max)` | |
| `TicketFormat` | `nvarchar` | |
| `DurationMinutes` | `int` NULL | |
| `IsActive` | `bit` | |
| `CreatedAt` | `datetime2` | |

### `Purchases`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `UserId` | `nvarchar(450)` | FK, **indexed** |
| `TicketingAdapterId`, `TicketOptionId` | `int` | FKs |
| `WalletTransactionId` | `int` NULL | FK, `SetNull` |
| `ExternalPurchaseId` | `nvarchar(max)` NULL | **Indexed** |
| `IdempotencyKey` | `nvarchar(64)` NULL | See below |
| `Amount` | `decimal(18,2)` | |
| `Currency` | `nvarchar(3)` | |
| `Status` | `nvarchar` | `Pending`/`Completed`/`Failed`/`Refunded` |
| `PurchasedAt` | `datetime2` | |
| `CompletedAt` | `datetime2` NULL | |
| `FailureReason` | `nvarchar(max)` NULL | |

**The idempotency index:**

```sql
CREATE UNIQUE INDEX IX_Purchases_UserId_IdempotencyKey
ON Purchases (UserId, IdempotencyKey)
WHERE [IdempotencyKey] IS NOT NULL;
```

The filter is essential — without it, only one keyless purchase per user could ever exist. With it, a
retried purchase collides at the database rather than charging the wallet twice. `TicketingManager`
catches SQL error 2601/2627 and replays the original result.

`Status = 'Pending'` with no ticket means the process died between the debit and the adapter call.
Recoverable, surfaced on the admin overview, no automatic sweep.

### `Tickets`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `PurchaseId` | `int` | FK |
| `ExternalTicketId` | `nvarchar(max)` NULL | **Indexed** |
| `Format` | `nvarchar` | |
| `Data` | `nvarchar(max)` | QR content, base64 PDF, or a reference code |
| `ValidFrom`, `ValidTo` | `datetime2` NULL | |
| `ActivatedAt` | `datetime2` NULL | |
| `Status` | `nvarchar` | |
| `JourneyId` | `int` NULL | FK, `SetNull` |
| `CreatedAt` | `datetime2` | |

> **`Tickets` has no `UserId`.** Ownership runs through `Purchase`. Every ownership check must join:
> ```csharp
> _db.Tickets.Where(t => _db.Purchases.Any(p => p.Id == t.PurchaseId && p.UserId == userId))
> ```
> This asymmetry with `ImportedTickets` is the single easiest place to introduce a cross-user bug.

### `ImportedTickets`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `UserId` | `nvarchar(450)` | FK. **Present here, unlike `Tickets`** |
| `OperatorGlobalId` | `nvarchar(128)` NULL | TransitInfoAPI OnestopId |
| `OperatorNameSnapshot` | `nvarchar(200)` NULL | Denormalised on purpose |
| `Source` | `nvarchar(32)` | |
| `Status` | `nvarchar(32)` | |
| `Verification` | `nvarchar(32)` | Always `Unverified` — nothing sets the others yet |
| `TicketName` | `nvarchar(200)` NULL | |
| `RouteDescription` | `nvarchar(500)` NULL | |
| `OriginName`, `DestinationName` | `nvarchar(200)` NULL | Structured — journey chaining needs these |
| `Price` | `decimal(18,2)` NULL | |
| `Currency` | `nvarchar(3)` NULL | |
| `ValidFrom`, `ValidTo` | `datetime2` NULL | Normalised to UTC on write |
| `RawPayload` | `nvarchar(8000)` NULL | Decoded barcode |
| `PayloadFormat` | `nvarchar` NULL | |
| `SourceFileBlobKey` | `nvarchar(max)` NULL | |
| `SourceFileContentType` | `nvarchar(100)` NULL | Sniffed, not declared |
| `DedupeHash` | `nvarchar(64)` NULL | SHA-256 hex |
| `JourneyId` | `int` NULL | FK, `SetNull` |
| `CreatedAt`, `UpdatedAt` | `datetime2` | |

Indexes:

```sql
IX_ImportedTickets_UserId_Status                     -- the list query

CREATE UNIQUE INDEX IX_ImportedTickets_UserId_DedupeHash
ON ImportedTickets (UserId, DedupeHash)
WHERE [Status] = 'Active' AND [DedupeHash] IS NOT NULL;
```

Both filter clauses do work. `Status = 'Active'` means a cancelled or expired ticket does not block
re-importing the same one. `DedupeHash IS NOT NULL` is what lets an explicitly-allowed duplicate
coexist — `AllowDuplicate` stores **no hash at all** rather than just skipping the pre-check.

`OperatorNameSnapshot` is denormalised so a ticket still reads correctly if the operator is renamed or
removed upstream. `MaxLength(8000)` on `RawPayload` keeps it out of `nvarchar(max)` — indexable and
in-row.

### `TicketUploads`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `UserId` | `nvarchar(450)` | FK |
| `BlobKey` | `nvarchar(128)` | **Unique index** |
| `FileType` | `nvarchar(32)` | Sniffed |
| `ContentType` | `nvarchar(100)` | |
| `SizeBytes` | `bigint` | |
| `ConsumedAt` | `datetime2` NULL | Set when a ticket claims it |
| `CreatedAt` | `datetime2` | |

Indexes: `BlobKey` unique, and `(UserId, ConsumedAt)`.

This table is **what makes it safe to let a client name a stored file**. The blob key is server-minted
and recorded against its owner, so create can verify the caller uploaded this exact file and has not
spent it. Unique rather than merely indexed, because the key must resolve to exactly one row.

`(UserId, ConsumedAt)` serves both the ownership lookup on create and the 24-hour expiry sweep for
uploads never turned into tickets.

### `Journeys`

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `UserId` | `nvarchar(450)` | FK |
| `Name` | `nvarchar(200)` | |
| `Notes` | `nvarchar(2000)` NULL | |
| `Status` | `nvarchar(32)` | |
| `StartsAt`, `EndsAt` | `datetime2` NULL | **Derived but stored** |
| `CreatedAt`, `UpdatedAt` | `datetime2` | |

Indexes: `(UserId, StartsAt)` for the list view, `(UserId, Status)` for filtering.

`StartsAt`/`EndsAt` are min/max of member tickets — denormalised so journeys sort and filter without
joining both ticket tables on every list query. `JourneyManager.RefreshDatesAsync` recomputes them on
every membership change.

Membership is a nullable FK on each ticket table rather than a polymorphic join table, because a
polymorphic join cannot be enforced by a foreign key.

### `UserSettings`

`Id`, `UserId` (FK, **unique**), `Theme`, `Language`, `NotificationsEnabled`, `MapStyle`, `UpdatedAt`.
Created lazily on first read.

### `AuditLogs`

`Id`, `UserId` (FK, `SetNull`), `Action`, `EntityType`, `EntityId`, `OldValues`, `NewValues`,
`CreatedAt` (**indexed** — the log is always read newest-first).

`OldValues`/`NewValues` are JSON stored as text — no schema, since the shape differs per entity type.

`SetNull` on the user FK is the point: the audit trail must survive the user it describes.

Recorded actions: `Register`, `RegisterAttemptOnExistingAccount`, `Login`, `LoginFailed`, `Logout`,
`TokenRefresh`, `RefreshTokenReuseDetected`, `PasswordChanged`, `WalletTopUp`, `CreateRole`,
`UpdateRolePermissions`, `DeleteRole`, `SetUserRole`, `EnableAdapter`, `DisableAdapter`.

---

## Migration history

| Migration | Date | What and why |
|---|---|---|
| `InitialCreate` | 2026-06-14 | Wallets, adapters, options, purchases, tickets, settings, audit |
| `AddIdentity` | 2026-07-12 | ASP.NET Identity tables |
| `AddImportedTickets` | 2026-07-25 | Import feature |
| `HardenImportedTickets` | 2026-07-26 | Max lengths, dedupe hash, the filtered unique index |
| `HardenRefreshTokenIndex` | 2026-07-27 | `nvarchar(max)` → `nvarchar(128)` so the unique index actually exists |
| `AddPurchaseIdempotencyKey` | 2026-07-27 | Filtered unique index preventing double-charges |
| `AddTicketUploadsAndEndpoints` | 2026-07-27 | `TicketUploads` — safe client-named files |
| `AddJourneys` | 2026-07-28 | `Journeys` plus `JourneyId` on both ticket tables |

The three `Harden*`/`Add*Key` migrations are worth reading as a group: each fixes a defect that was
invisible in code review (an index that was never created, a double-charge window, an unauthenticated
file reference) and only became apparent from the database's behaviour.

---

## Operational notes

**Money.** All monetary columns are `decimal(18,2)`. Balance changes go through conditional SQL
`UPDATE`s, never read-then-write. There is no currency conversion anywhere — a purchase whose currency
differs from the wallet's is rejected.

**Deletion.** `Restrict` everywhere means deleting a user fails while any wallet, purchase, imported
ticket or journey references them. There is **no account-deletion path**; this would need to be built
deliberately, and the audit trail's `SetNull` shows the intended shape.

**Time.** Every timestamp is UTC. `ImportedTicketManager.ToUtc` normalises incoming values, treating
`Unspecified` as UTC — a judgement call matching what the MAUI client sends.

**Growth.** `WalletTransactions`, `AuditLogs` and `Purchases` grow without bound. No retention or
archival policy exists. `AuditLogs.CreatedAt` is indexed, which is what a future purge would use.
