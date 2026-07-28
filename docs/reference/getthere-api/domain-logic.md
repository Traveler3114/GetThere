# GetThereAPI — Domain Logic

The managers hold every business rule in the service. This document explains the reasoning behind the
non-obvious ones: the money path, deduplication, journey grouping, and the background sweep that ties
them together.

| Manager | Owns |
|---|---|
| `AuthManager` | Registration, login, refresh rotation, password change |
| `TokenManager` | JWT minting, refresh token generation and hashing |
| `WalletManager` | Balance, top-up, wallet creation |
| `TicketingManager` | The purchase path |
| `ImportedTicketManager` | Imported ticket CRUD, deduplication, UTC normalisation |
| `TicketUploadManager` | File intake, extraction dispatch, abandoned-upload sweep |
| `JourneyManager` | Trip grouping, suggestions, status roll-up |
| `MapManager` | Transit data proxy and caching |
| `AdminManager` | Platform KPIs, purchase feed, adapter health |
| `RolePermissionManager` | Roles and permission claims |
| `ProfileManager`, `UserSettingsManager` | Self-service profile and settings |

---

## The money path

This is the part of the system where being wrong costs a user real money, and it is written
defensively throughout. Two principles run through all of it:

1. **Never read-then-write a balance.** Every balance change is a single conditional SQL `UPDATE`.
2. **Never hold a transaction across a network call.** External calls fail slowly; database locks
   held while they do would serialise the whole service.

### Why raw SQL for balance changes

Both `WalletManager.TopUpAsync` and `TicketingManager.PurchaseTicketAsync` use
`ExecuteSqlInterpolatedAsync` rather than loading the entity and assigning:

```sql
UPDATE Wallets SET Balance = Balance - {price}, UpdatedAt = {now}
WHERE Id = {walletId} AND Balance >= {price}
```

The `Balance = Balance - x` form makes the arithmetic happen inside the database, so two concurrent
purchases cannot both read the same starting balance. The `AND Balance >= {price}` clause makes the
sufficiency check part of the same atomic statement — **`rowsAffected == 0` is how insufficient funds
is detected.** Checking the balance in C# first and then updating would leave a window where both
requests pass the check.

`ExecuteSqlInterpolatedAsync` parameterises the interpolated values, so this is not string
concatenation and is not injectable.

The consequence, and it has bitten this code before: **raw SQL does not refresh EF's change tracker**.
The tracked entity still holds the pre-update balance. Every one of these call sites therefore re-reads
the committed value:

```csharp
var balanceAfter = await _db.Wallets.AsNoTracking()
    .Where(w => w.Id == wallet.Id).Select(w => w.Balance).FirstAsync(ct);
```

Skipping that re-read is what made both the ledger row and the API response report the balance from
*before* the top-up.

### The purchase path

`TicketingManager.PurchaseTicketAsync` runs in three stages, and the boundaries between them are the
whole point.

#### Stage 1 — validate before any money moves

Everything that can fail without touching money is checked first:

- Adapter exists and is active
- **An SDK implementation is registered for the adapter's type.** This is resolved *up front,
  deliberately*: with no implementation registered, every purchase would otherwise take the money and
  then fail. → 503 `ADAPTER_NOT_REGISTERED`
- Option exists, is active, and belongs to that adapter
- Wallet exists
- **Wallet currency matches option currency.** There is no conversion service, so a cross-currency
  purchase would silently debit the raw numeric amount — 100 USD taking 100 EUR. → 400
  `CURRENCY_MISMATCH`
- If an idempotency key was supplied, an earlier purchase with it is replayed

#### Stage 2 — debit, commit, release

Inside one transaction: the conditional `UPDATE`, a `WalletTransaction` ledger row of type
`TicketPurchase` with a **negative** amount, and a `Purchase` row with status `Pending`.

Then the transaction **commits and closes** — before the adapter is called. No SQL transaction and no
wallet row lock is held across an outbound HTTP request.

If the `Purchase` insert hits the unique index on `(UserId, IdempotencyKey)`, another request with the
same key won the race: roll back, then try to replay that request's result.

#### Stage 3 — call the adapter with nothing open

On success: record the `Ticket`, set the purchase `Completed`, stamp `CompletedAt`.

On **any** failure — thrown exception, `Success = false`, or a null ticket — call `RefundAsync`.

The refund is written as a **compensating credit plus a `Refund` ledger row**, not by deleting the
debit. Deleting would make the wallet history lie about what happened; the ledger should show the
charge and the reversal. The purchase is marked `Refunded` with the reason in `FailureReason`.

#### The recoverable failure state

A purchase left `Pending` means the process died between stages 2 and 3 — money taken, no ticket, no
refund. This is a known, deliberate trade-off: the alternative (holding a transaction across the
adapter call) is worse. The state is recoverable and observable — `AdminStats.PendingPurchases` and
`OldestPendingPurchaseAt` surface it on the admin overview. There is currently **no automatic
reconciliation sweep**; resolving one is a manual operation.

#### Idempotency, in detail

| Situation | Result |
|---|---|
| Key seen, purchase produced a ticket | The original `TicketResponse` is returned. No new charge |
| Key seen, no ticket (original failed and was refunded) | 409 `DUPLICATE_PURCHASE` with the original failure reason |
| Key racing concurrently | Unique-index violation → rollback → replay if possible, else 409 |
| No key | No protection. A retry charges again |

The index is `HasFilter("[IdempotencyKey] IS NOT NULL")` — filtered, so rows without a key are
unconstrained. Without the filter, only one keyless purchase per user could ever exist.

The second row of that table is why a failed purchase does not just silently succeed on retry: the key
is consumed by the *attempt*, not by the success, so the client must generate a fresh key to genuinely
retry.

#### One subtlety in the response

After creating the ticket, the manager re-reads it with `Include`s rather than mapping the in-memory
entity. `TicketMapper.ToTicketResponse` dereferences `Purchase.TicketOption` and `Purchase.Adapter`,
and change-tracker fixup cannot populate those here — the option was read `AsNoTracking`. Mapping the
in-memory object throws a null reference.

---

## Imported tickets

### Deduplication, and why it is a hash

Users import the same ticket twice — from the confirmation email, then from the wallet pass. Detecting
that requires comparing tickets for equality, which needs a canonical form. `ComputeDedupeHash`
produces a SHA-256 over either:

- the trimmed `RawPayload`, when there is one — a barcode payload *is* the ticket's identity; or
- a **length-prefixed** concatenation of `OperatorGlobalId`, `RouteDescription`, `ValidFrom`,
  `ValidTo`, `Source`, `TicketName`, `Price`, `Currency`.

Two details in that fallback are deliberate:

**Length-prefixing rather than delimiter-joining.** A bare `|` separator let a value containing the
delimiter shift the field boundary — `TicketName = "Zagreb|Rijeka"` could collide with a different
ticket whose route happened to start `"Rijeka"`. Each field is written as `{length}:{value}|`, so a
value cannot forge a boundary.

**Price is part of the identity.** Two otherwise-identical tickets bought at different fares are
different tickets; they were previously collapsed into one.

Dates are hashed **after** UTC normalisation, formatted `"O"`. Hashing before normalising would give
the same logical ticket two different hashes depending on how the caller expressed the offset.

Enforcement is two-layer:

1. A pre-check query returning a friendly 409 naming the clashing ticket id.
2. A unique filtered index `(UserId, DedupeHash) WHERE Status = 'Active' AND DedupeHash IS NOT NULL`,
   which catches the race between the check and the insert.

`AllowDuplicate: true` stores **no hash at all** rather than only skipping the pre-check. The index is
filtered on `DedupeHash IS NOT NULL` precisely so an accepted duplicate can coexist — skipping only
the pre-check would still hit the index and fail.

The filter on `Status = 'Active'` means a cancelled or expired ticket does not block re-importing the
same one, which is correct: those are no longer live.

> The index filter compares `Status` to the **string** `'Active'`. That works because `AppDbContext`
> converts every enum to a string globally (see [database.md](../db/getthere-schema.md)). Switching to
> integer enum storage would silently break this index.

### UTC normalisation

`ToUtc` forces every incoming `DateTime` to UTC:

| Incoming `Kind` | Result |
|---|---|
| `Utc` | unchanged |
| `Local` | `ToUniversalTime()` |
| `Unspecified` | **relabelled as UTC**, value untouched |

This exists because `TicketExpiryWorker` compares `ValidTo` against `DateTime.UtcNow`, but
`System.Text.Json` yields `Unspecified` for an offset-less timestamp and SQL Server's `datetime2`
carries no kind. Without normalisation a naive payload is stored verbatim and expires off by the
caller's UTC offset.

Taking `Unspecified` at face value as UTC is a **judgement call**, chosen because it is what the
existing MAUI client sends. A third-party client sending local wall-clock time without an offset will
have its tickets expire at the wrong moment.

---

## Journeys

A journey is a trip: an outbound rail leg, a city transit pass, the return. Membership spans **both**
ticket tables, because a real trip mixes tickets bought in the app with tickets imported from
elsewhere.

### Why a nullable FK rather than a join table

Membership is a nullable `JourneyId` column on each ticket table, not a polymorphic join table. A
polymorphic join (`journey_id`, `ticket_type`, `ticket_id`) cannot be enforced by a foreign key — the
database could not stop a row pointing at a nonexistent ticket. Two nullable FKs can be enforced. The
cost is one extra column and two `Include`s instead of one.

### The ownership asymmetry

`ImportedTicket` has a `UserId`. **`Ticket` does not** — ownership runs through `Purchase`. Every
membership check on purchased tickets therefore has to join:

```csharp
private IQueryable<Ticket> OwnedTicketsQuery(string userId) =>
    _db.Tickets.Where(t => _db.Purchases.Any(p => p.Id == t.PurchaseId && p.UserId == userId));
```

Filtering `Ticket` directly by user is impossible, and forgetting the join is how a cross-user
membership bug would appear. This is the single most important thing to know before touching journey
code.

### Derived dates, stored anyway

`StartsAt`/`EndsAt` are the min/max of member tickets' validity — derived data, deliberately
denormalised onto the journey row so journeys can be sorted and filtered without joining every ticket
table on every list query.

`RefreshDatesAsync` recomputes them whenever membership changes. **Ordering matters and is commented
in the code**: membership must be saved *before* recomputing, because `RefreshDatesAsync` queries the
tickets back out and would not see pending in-memory changes. Both `AddTicketsAsync` and
`RemoveTicketsAsync` follow save → refresh → save.

### Status roll-up

Status is a function of the legs, computed by `RollUpStatusesAsync` from the background worker:

| Condition | Status |
|---|---|
| `StartsAt` is null | `Planned` |
| `EndsAt` in the past | `Completed` |
| `StartsAt` in the past | `Active` |
| otherwise | `Planned` |

`Cancelled` is excluded from the query entirely — it is a user decision and is never rolled over. This
is why `PATCH /journeys/{id}` rejects every status except `Cancelled`: anything else would be
overwritten by the next sweep.

### Journey suggestions

`SuggestAsync` proposes groupings over imported tickets that are unassigned, `Active`, and have a
`ValidFrom`. It takes the earliest 200, then greedily chains.

A ticket joins the current chain if **either** condition holds:

- **Close in time** — it starts within ±24h (`SuggestionWindow`) of the chain's current end; or
- **Continues the route** — its origin matches the chain's current destination.

The second condition is what makes a multi-day trip group correctly, and it only became possible once
extraction started populating structured origin/destination rather than free text.

Groups of one are discarded — a single ticket is not a trip.

#### `Connects` — matching place names across operators

Two operators write the same station differently: `"Zagreb Gl. Kol."` and `"Zagreb Glavni Kolodvor"`.
Matching runs in two passes:

1. Strip everything but letters and digits, lowercase, then compare for equality or prefix.
2. Failing that, compare the **leading word** only — operators abbreviate the qualifier but almost
   never the place.

The leading-word fallback requires ≥3 characters, because two letters is initials or noise rather than
a place name.

Matching on the city is arguably the right granularity anyway: arriving at one Vienna terminus and
departing from another still continues the same trip. The trade is real, though — two unrelated
tickets that both start in Zagreb will chain. That is acceptable **only because suggestions are never
applied automatically**; the user sees the proposal and its `reason` before anything happens.

Suggestions cover imported tickets only; `TicketIds` is always empty in the response.

---

## The background worker

`TicketExpiryWorker` is a `BackgroundService` running every `TicketExpiry:CheckIntervalHours` (default
1 hour, floored at 1 minute so a configured `0` cannot spin against the database).

It does four things per pass:

1. `ImportedTicket` `Active` with `ValidTo < now` → `Expired`
2. `Ticket` `Active` with `ValidTo < now` → `Expired`
3. `TicketUploadManager.PurgeAbandonedAsync()` — uploads never turned into a ticket, older than 24h
4. `JourneyManager.RollUpStatusesAsync()`

Design points worth keeping:

**It sweeps first, then waits.** Delaying up front leaves tickets that expired while the service was
down showing as `Active` for a full interval after every restart.

**Steps 1 and 2 use `ExecuteUpdateAsync`** — a set-based SQL `UPDATE`, not load-modify-save. The
number of expired tickets is unbounded; materialising them all is not.

**Step 2 was missing.** The worker only swept imported tickets, so a *bought* ticket stayed `Active`
forever once its window closed.

**A failure in one pass does not stop the worker.** The body is wrapped in try/catch: `OperationCanceledException` breaks the loop (shutdown), anything else is logged and the loop continues.
A transient database blip must not silently kill expiry for the process lifetime.

**Step 3 tolerates individual failures.** A blob that will not delete is logged and its row is still
removed — otherwise the sweep retries it forever and never reaches the rest of the backlog.

---

## Admin analytics

`AdminManager.GetStatsAsync` computes KPIs over a rolling window and compares each against the window
immediately before it. Two implementation notes:

**Aggregation happens in SQL.** Pending purchases are counted with `CountAsync` and `MinAsync` rather
than materialising the list and calling `.Count()`/`.Min()` in memory — that grows unbounded with the
backlog, which is exactly the situation the metric exists to detect.

**The sparkline is always 7 buckets.** The SQL `GROUP BY` returns only days that had purchases, so the
result is projected onto a fixed 7-day range with zeros filled in. Otherwise a quiet day would shorten
the array and shift the chart.

Adapter status is derived rather than stored:

| Status | Condition |
|---|---|
| `Disabled` | `!IsActive` |
| `Unregistered` | Active, but no SDK implementation registered for its type |
| `Idle` | No purchases in the window |
| `Failing` | Failure ratio ≥ 50% |
| `Degraded` | Failure ratio > 2% (`DegradedFailureRatio`) |
| `Ok` | Otherwise |

`Unregistered` is checked before traffic, because an adapter with no implementation is broken whether
or not anyone has tried to use it. `HasApiKey` is exposed as a **boolean**; the key itself never leaves
the server.

---

## Ticketing adapters (the SDK)

`ITicketingAdapter` is the integration point for a transit operator's ticketing system:

```csharp
string Name { get; }
string AdapterType { get; }                 // slug, e.g. "hzpp.v1"
List<RequiredInput> RequiredInputs { get; } // fields the operator needs, with IsSensitive
Task<PurchaseResult> PurchaseAsync(PurchaseRequest, CancellationToken);
Task<TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken);
```

The split between database and code is deliberate:

- The **`TicketingAdapter` row** holds configuration — name, base URL, API key, active flag, and the
  `TransitInfoGlobalId` binding it to an operator in TransitInfoAPI.
- The **`AdapterRegistry` entry** holds the implementation, keyed by `AdapterType`.

A row can exist with no code behind it, which is exactly the `Unregistered` health status. That is why
`PurchaseTicketAsync` resolves the implementation in stage 1 — otherwise every purchase against such a
row would take money and then fail.

`AdapterBase` supplies `HttpClient`, a trimmed base URL, and `CreateRequest`, which attaches
`X-Api-Key` when a key is configured.

`AdapterRegistry` is registered as a **singleton with no populated entries**. Nothing in the current
codebase calls `Register`, so in a default deployment every purchase returns 503
`ADAPTER_NOT_REGISTERED`. The ticketing catalogue and purchase path are complete; the operator
integrations themselves are not yet written.

`TransitInfoGlobalId` is the join between the two systems — it is what lets
`MapOperatorResponse.HasTicketing` tell the map which operators can actually sell a ticket.
