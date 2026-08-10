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

## The same bug again, on WalletTransactions — MIGRATION STILL OWED

**Found:** 2026-08-10, during the full-solution audit.

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

`AppDbContext.OnModelCreating` now sets `HasMaxLength(32)` on `Type`, `HasMaxLength(64)` on
`ReferenceId`, and declares the filtered unique index on `(Type, ReferenceId)`.

**The migration has not been generated.** It was written in a container with no .NET SDK, so
`dotnet ef` could not be run, and `AGENTS.md` rules out hand-editing migrations and the model
snapshot. Until someone runs:

```bash
cd GetThereAPI
dotnet ef migrations add AddWalletTransactionRefundIndex
dotnet ef database update
```

the model and the database disagree: the index does not exist and the refund guard keeps scanning.
Nothing regresses in the meantime — the guard is still correct, just as slow as it was — but the
performance fix is not live until the migration is applied.

Two things to check when generating it, because the filter is not free:

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
