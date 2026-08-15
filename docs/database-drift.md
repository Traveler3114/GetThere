# GetThereDB is out of sync with the EF migrations

**Found:** 2026-07-27, while running the verification pass against a local `GetThereDB`.

**RESOLVED 2026-07-27** — the database was dropped and rebuilt from the migrations (option 1 below),
on request. The legacy tables are gone, `Purchases` / `TicketingAdapters` / `TicketOptions` /
`UserSettings` exist, and `Wallets` has `Currency` / `CreatedAt` / `UpdatedAt`. All four endpoints
listed below were re-checked and return 200.

The data lost was local test data only: 5 users (3 of them probe accounts created during the audit),
1 wallet, 6 wallet transactions, 4 tickets, 1 imported ticket and 12 legacy country/city rows.

The history below is kept because **any other environment stamped the same way has the same
problem**, and the diagnosis is what makes that recognisable.

---

## What is wrong

`__EFMigrationsHistory` says all four migrations are applied:

```
20260614073834_InitialCreate
20260712115942_AddIdentity
20260725113832_AddImportedTickets
20260726132802_HardenImportedTickets
```

The schema does not match any of them. The database holds tables from an **older generation of the
application** and is missing tables the current model requires:

| | Tables |
|---|---|
| **Present but not in `AppDbContext`** | `Cities`, `Countries`, `MobilityProviders`, `MobilityProviderCity`, `MobilityProviderCountry`, `PaymentProviders`, `Payments`, `TransitOperators`, `TransitOperatorTransportType`, `TransportTypes` |
| **Missing but required** | `Purchases`, `TicketingAdapters`, `TicketOptions`, `UserSettings` |

Existing tables are also the wrong shape. `Wallets` has `LastUpdated` and no `Currency`, while
`InitialCreate` defines `Currency`, `CreatedAt` and `UpdatedAt`. `WalletTransactions` has
`Timestamp`/`TicketId` instead of `CreatedAt`, `BalanceBefore`, `BalanceAfter` and `ReferenceId`.

The migration history was evidently stamped onto a pre-existing legacy database rather than the
schema being built from the migrations.

## What it breaks right now

Verified against a running instance:

| Endpoint | Result | Cause |
|---|---|---|
| `GET /wallet` | 500 | `Invalid column name 'Currency' / 'CreatedAt' / 'UpdatedAt' / 'BalanceBefore' / 'BalanceAfter' / 'ReferenceId'` |
| `GET /admin/purchases` | 500 | `Invalid object name 'Purchases'` |
| `GET /admin/stats` | 500 | `Invalid object name 'Purchases'` |
| `GET /admin/adapters` | 500 | `Invalid object name 'TicketingAdapters'` |

Auth, profile, audit, admin users and imported tickets all work.

This also corroborates **C2** in `money-path-defects.md`: the purchase flow cannot ever have been
run end-to-end against this database, which is consistent with no `ITicketingAdapter`
implementation having been written.

## The related index bug this uncovered

`IX_RefreshTokens_Token` was declared in the model from `AddIdentity` onwards but never existed in
the database, because `RefreshToken.Token` was unbounded and therefore `nvarchar(max)` — SQL Server
cannot index that. Every token refresh was a full table scan. Fixed in
`20260727092919_HardenRefreshTokenIndex`, which bounds the column to 128 and creates the index as
unique. That migration **is** applied and verified.

## The same bug again, on WalletTransactions — RESOLVED

**Found:** 2026-08-10, during the full-solution audit.

**RESOLVED 2026-08-15** — `20260815145411_AddWalletTransactionRefundIndex` applied.
`Type` is `nvarchar(32)`, `ReferenceId` is `nvarchar(64)`, and
`IX_WalletTransactions_Type_ReferenceId` exists as unique with filter
`([ReferenceId] IS NOT NULL AND [Type]='Refund')`, confirmed in `sys.indexes` rather than only in
the model.

Both pre-checks below passed, but **vacuously**: the local `WalletTransactions` was empty (0 rows),
because this database was rebuilt from the migrations on 2026-07-27. So there were no duplicate
refunds to find and no length to exceed. That is not evidence about any other environment — the
narrowing `AlterColumn` and the unique index are exactly the two statements that fail on data, and
an environment with real refund history must still run the two queries before applying it.

The same visit found this database nine migrations behind (only `InitialCreate` and `AddIdentity`
were in `__EFMigrationsHistory`, against eleven migration files). Those were applied first, after
checking `RefreshTokens` for the duplicates and over-length tokens that
`HardenRefreshTokenIndex` would reject — max token length 44, no duplicates across 9 rows.

The history below is kept for the reasoning, which still applies to the next index like it.

`WalletTransaction.Type` and `WalletTransaction.ReferenceId` had no configured length, so both were
`nvarchar(max)` and neither could be indexed — exactly the shape of the `RefreshTokens.Token` bug
above, two tables over.

What made it worse than a missing index: `TicketingManager.RefundAsync` guards against paying a
refund twice by reading

```sql
SELECT TOP 1 1 FROM WalletTransactions WITH (UPDLOCK, HOLDLOCK)
WHERE Type = @type AND ReferenceId = @reference
```

With no index to seek, that range lock is taken over a **full table scan**, so every refund
serialised against every other refund and blocked inserts of any wallet transaction for the length
of its transaction. The code comment beside it already named the filtered unique index as the
durable fix; what it did not say is that the missing lengths were what made that index impossible.

The fix was `HasMaxLength(32)` on `Type`, `HasMaxLength(64)` on `ReferenceId`, and a filtered unique
index on `(Type, ReferenceId)`. Why it sat unapplied for five days is worth recording, because it
caught us out.

It was applied on 2026-08-10 without a migration, on the assumption that a model change ahead of its
migration is inert — the index simply would not exist yet. **That assumption is wrong.** EF Core
raises `PendingModelChangesWarning` as an *error* from inside `Database.Migrate()`, and all three
database-backed fixtures call it in their constructors. The result was 54 failed tests, every one of
them:

```
System.InvalidOperationException : An error was generated for warning
'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning':
The model for context 'AppDbContext' has pending changes.
```

So the model edit was reverted until an SDK was available. **The model change and its migration have
to land in the same commit**, which is how it was finally done:

```bash
dotnet ef migrations add AddWalletTransactionRefundIndex --project GetThereAPI/GetThereAPI.csproj
dotnet ef database update --project GetThereAPI/GetThereAPI.csproj
```

The same trap applied to the `TransitInfoAPI` column sizes in the next section, for the same reason.

Until it landed the refund guard kept scanning: still correct, still slow.

Two things to check before applying it anywhere else, because the filter is not free:

- The filter `[ReferenceId] IS NOT NULL AND [Type] = 'Refund'` depends on `Type` being stored as its
  enum **name**, which it is: `AppDbContext` applies `EnumToStringConverter` to every enum property
  in the model. If that convention ever changes, this filter silently matches nothing.
- Any existing database with two `Refund` rows sharing a `ReferenceId` will fail to create the unique
  index. That is precisely the duplicate-refund the guard exists to prevent, so if it fails, the rows
  it names are a real double-credit worth investigating before deleting anything.

## Options

1. **Recreate the database** (loses all local data — the usual choice for a dev box):
   ```bash
   dotnet ef database drop --force --project GetThereAPI/GetThereAPI.csproj
   ```
   ```bash
   dotnet ef database update --project GetThereAPI/GetThereAPI.csproj
   ```
2. **Keep the data** — write a corrective migration that drops the legacy tables, adds the missing
   ones and reshapes `Wallets`/`WalletTransactions`, migrating existing rows
   (`LastUpdated` → `UpdatedAt`, `Timestamp` → `CreatedAt`, backfilling `Currency` with `'EUR'`).
   Considerably more work, only worth it if the local wallet rows matter.

Whichever you pick, check any other environment before assuming it is only local: if a shared or
staging database was stamped the same way, it has the same broken endpoints.

## nvarchar(450) on every indexed string column in TransitInfoAPI — MIGRATION OWED

**Found:** 2026-08-12, audit round 2.

The mirror image of the two bugs above. `TransitDbContext` declares 7 `HasMaxLength` calls for ~157
string properties, and every string column it indexes has no configured length — so EF widened each
to `nvarchar(450)`, the 900-byte index-key limit. The indexes therefore exist (SQL Server refuses to
index `nvarchar(max)`, so the alternative would have been a failed migration), but each key reserves
up to 900 bytes for content that is tens of characters:

| Entity | Column | Now | Intended | Real content |
|---|---|---|---|---|
| `StopTime` | `RawStopId` | `nvarchar(450)` | 128 | GTFS stop id |
| `Trip` | `TripId` | `nvarchar(450)` | 128 | GTFS trip id |
| `RawStop` | `RawStopId` | `nvarchar(450)` | 128 | GTFS stop id |
| `FeedVersion` | `Sha1` | `nvarchar(450)` | 64 | exactly 40 hex chars |
| `Country` | `IsoCode` | `nvarchar(450)` | 8 | **2 chars**, unique index |
| `Operator`, `CanonicalStation`, `CanonicalRoute` | `OnestopId` | `nvarchar(450)` | 128 | short slug |
| `Feed` | `FeedId` | `nvarchar(450)` | 128 | short slug |
| `Feed` | `OnestopId` | `nvarchar(max)` | 128 | the one `OnestopId` with no index |

`StopTimes` and `Trips` are the largest tables in the system — the import path streams "a million
stop_times rows" — and both carry an index keyed on one of these.

GetThereAPI already reached this conclusion, on `Purchase.Status`: *"letting EF widen it to the
450-char key limit would put ~900 bytes per row in an index over four short words."* The project that
never applied it is the one with the big tables.

The sizes are recorded as a comment in `TransitDbContext.OnModelCreating` rather than applied, for
the reason in the section above: a model change without its migration turns the suite red. Generate
both together:

```bash
cd TransitInfoAPI
dotnet ef migrations add SizeIndexedStringColumns
dotnet ef database update
```

Two things to check when doing it:

- A length below what a live row already holds fails the migration. Query the current maxima first
  (`SELECT MAX(LEN(RawStopId)) FROM StopTimes` and so on) and raise the target if any feed carries
  something unusual.
- The point of the change is index size, so measure it. `sys.dm_db_partition_stats` before and after
  on `IX_StopTimes_RawStopId`, `IX_Trips_FeedVersionId_TripId` and `IX_Countries_IsoCode` is the
  evidence that it worked.
