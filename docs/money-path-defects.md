# Money-Path Defects — Wallet & Ticket Purchase

**Status: FIXED, 2026-07-27.** This document was written first as a defect report while the area was
off-limits; it is kept as the record of what was wrong, what was done, and what is still owed.

**Files:** `GetThereAPI/Managers/TicketingManager.cs`, `GetThereAPI/Managers/WalletManager.cs`,
`GetThereAPI/Controllers/WalletController.cs`, `GetThereAPI/Sdk/`,
`GetThereShared/Contracts/WalletContract.cs`.
**Tests:** `tests/GetThere.Tests/Money/` — nine tests against a real SQL Server database.

---

## Summary

| ID | Was | Now |
|----|-----|-----|
| C1 | A failed purchase debited the wallet and committed the debit — no ticket, no refund | Debit is reversed with a compensating `Refund` ledger row; purchase becomes `Refunded` |
| C2 | No `ITicketingAdapter` implementation existed, so **every** purchase took the C1 path | The registry is consulted *before* the debit; unregistered → 503 with no money moved |
| C4 | `POST /wallet/topup` credited any amount, gated on a permission every user holds | Admin-only `wallets.topup`, capped at 1000, validated, audit-logged |
| H8 | Top-up returned the pre-top-up balance | Balance is re-read after the update; response is freshly loaded |
| M1 | A SQL transaction and the wallet row lock were held across an outbound HTTP call | Three stages; the adapter is called with no transaction open |
| M2 | No currency check — 100 USD debited 100 EUR | Rejected with `CURRENCY_MISMATCH` |
| M3 | No idempotency — a client retry double-charged | `Idempotency-Key` header, unique per user, replays the original ticket |
| M5 | Purchased tickets never left `Active` | `TicketExpiryWorker` now sweeps `Tickets` as well as `ImportedTickets` |

---

## The shape of the fix

`PurchaseTicketAsync` runs in three stages:

1. **Validate before touching money.** Adapter exists and is active, an implementation is registered,
   the option exists, the wallet exists, currencies match, and the idempotency key has not already
   been used. Nothing is debited until all of that passes.
2. **Debit and commit.** The conditional atomic `UPDATE ... WHERE Balance >= price` still prevents
   double-spend, and the debit, its ledger row and a `Pending` purchase commit together. The
   transaction then **closes**.
3. **Call the adapter, then settle.** On success the ticket is written and the purchase completed.
   On any failure — a declined result or a thrown exception — the debit is reversed and the purchase
   marked `Refunded`.

A purchase stuck at `Pending` now means the process died between stages 2 and 3. That is a
recoverable state, and `AdminManager.GetStatsAsync` already reports `PendingPurchases` and
`OldestPendingPurchaseAt`.

### Two latent bugs surfaced by the rewrite

- **`TicketMapper.ToTicketResponse` dereferenced `t.Purchase.TicketOption`**, which is never
  populated because the option is read `AsNoTracking`. Every successful purchase would have thrown a
  `NullReferenceException` at the last step. It was invisible only because purchases always failed
  earlier at C2. The ticket is now re-read with its navigations before mapping.
- **`RefreshToken.IsActive` is a computed, unmapped property** used inside `.Where(...)`, which EF
  cannot translate. It made `POST /auth/change-password` return 500. Fixed at all four call sites
  across both APIs.

---

## Still owed

- **There is still no `ITicketingAdapter` implementation.** Purchases return 503
  `ADAPTER_NOT_REGISTERED` rather than silently taking money. Shipping a real adapter is a feature,
  not a fix, and no fake one was added — a stub that pretends to issue tickets is exactly the
  problem that was just removed. The tests use a double, in the test project.
- **`/wallet/topup` still credits without taking payment.** It is admin-only for that reason. Wiring
  a provider means charging first and crediting only on confirmed settlement; keep the amount cap
  and the idempotency requirement when doing so, and move `wallets.topup` into
  `PermissionKeys.UserRoleDefaults` only at that point.
- **No reconciliation worker for stranded `Pending` purchases.** If the process dies between stages
  2 and 3, the row needs either completing or refunding. The admin console surfaces the oldest one;
  nothing acts on it yet.
- **Refunds are not idempotent against a duplicate adapter callback.** There is no callback endpoint
  today, so this only matters once one exists.
