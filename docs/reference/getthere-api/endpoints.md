# GetThereAPI — Endpoint Reference

Every controller carries a class-level `[Authorize]`, so **the default is authenticated**; anonymous
access is opt-in per action via `[AllowAnonymous]`. Each action then adds
`[Authorize(Policy = PermissionKeys.X)]` on top. Both apply — the class-level attribute establishes
"you must be signed in", the action-level one establishes "and you must hold this permission".

Reminder from [architecture.md](architecture.md): **the Admin role satisfies every policy**, so the
permission column below describes what a *non-admin* needs.

Errors are RFC 9457 problem JSON. The `title` field carries the error code when one was set,
otherwise the message.

---

## `/auth` — AuthController

Rate-limited to **10 requests/minute per IP** (`[EnableRateLimiting("Auth")]`), because these are the
credential-guessing endpoints.

| Method | Route | Auth | Body | Returns |
|---|---|---|---|---|
| POST | `/auth/register` | Anonymous | `RegisterRequest` | `{ message: "USER_REGISTERED" }` |
| POST | `/auth/login?rememberMe=false` | Anonymous | `LoginRequest` | `LoginResponse` |
| POST | `/auth/refresh` | Anonymous | `RefreshTokenRequest` | `RefreshTokenResponse` |
| POST | `/auth/logout` | Authenticated | `RefreshTokenRequest` | `{ message: "LOGGED_OUT" }` |
| POST | `/auth/change-password` | Authenticated | `ChangePasswordRequest` | `{ message: "PASSWORD_CHANGED" }` |

**`register` always returns 200**, even when the address is already registered — it must not reveal
whether an account exists. See [architecture.md](architecture.md#registration-does-not-confirm-whether-an-address-exists).

**`login`** captures `User-Agent` as `DeviceInfo` and `RemoteIpAddress` as `IpAddress` on the refresh
token row. Both are **forensics, not controls** — the IP was enforced on refresh until 2026-07-31 and
now only produces a `RefreshAddressChanged` audit entry; a stolen token is caught by reuse detection
instead. See [architecture.md](architecture.md#the-address-is-recorded-not-enforced).
`rememberMe` is a **query parameter, not part of the body** — easy to miss when hand-writing a call.

**`change-password`** revokes every active refresh token for the user afterwards. Changing a password
because you believe it was compromised has to sign out the other party's session too, or it achieves
nothing.

**`logout`** is intentionally forgiving: an unknown or already-revoked token still returns success.
There is nothing useful to tell a caller who is trying to end a session that is already ended.

| Error code | Status | Cause |
|---|---|---|
| `INVALID_CREDENTIALS` | 401 | Unknown address or wrong password — deliberately the same code for both |
| `REFRESH_TOKEN_EXPIRED` | 401 | Expired, revoked, replayed, or belonging to a locked account — deliberately indistinguishable |
| `USER_NOT_FOUND` | 404 | Change-password against a deleted user |

Failed logins, successful logins, refreshes, reuse detections and password changes all write
`AuditLog` rows.

---

## `/tickets` — TicketingController

Purchased tickets — bought in-app through a ticketing adapter. Distinct from imported tickets.

| Method | Route | Permission | Returns |
|---|---|---|---|
| GET | `/tickets/options` | `tickets.view` | `List<TicketOptionResponse>` |
| GET | `/tickets` | `tickets.view` | `List<TicketResponse>` |
| POST | `/tickets/purchase` | `tickets.create` | `TicketResponse` (201) |

`/tickets/options` returns only options whose adapter row is active, ordered by price. It is **not**
user-scoped — it is the catalogue.

### `POST /tickets/purchase`

```
Header:  Idempotency-Key: <8–64 chars>   (optional but strongly recommended)
Body:    { "adapterId": 1, "optionId": 4 }
```

The body carries **no amount**. The server prices from the option row, so a client cannot name its own
price.

The `Idempotency-Key` header is what makes a retry safe. Mobile clients retry on a dropped connection,
and a retry must not double-charge. Sending the same key twice returns the original ticket. See
[domain-logic.md](domain-logic.md#the-purchase-path) for the full three-stage flow.

| Error code | Status | Meaning |
|---|---|---|
| `ADAPTER_NOT_REGISTERED` | 503 | Adapter row exists but no SDK implementation is registered for its type |
| `CURRENCY_MISMATCH` | 400 | Wallet currency ≠ option currency; there is no conversion service |
| `WALLET_NOT_FOUND` | 404 | Call `POST /wallet/ensure` first |
| `INSUFFICIENT_BALANCE` | 400 | Checked atomically in SQL, not read-then-write |
| `DUPLICATE_PURCHASE` | 409 | Key reused, and the original attempt did not produce a ticket |
| `ADAPTER_FAILED` | 502 | Adapter threw. **Balance already restored** |
| `PURCHASE_FAILED` | 400 | Adapter returned failure. **Balance already restored** |

The last two matter for client UX: the money is back before the error reaches the user, so the
message should say so rather than telling them to check their balance.

---

## `/wallet` — WalletController

| Method | Route | Permission | Returns |
|---|---|---|---|
| GET | `/wallet` | `wallets.view` | `WalletResponse`, 404 if none |
| POST | `/wallet/topup` | **`wallets.topup`** | `WalletResponse` (201) |
| POST | `/wallet/ensure` | `wallets.view` | `WalletResponse` (201) |

`GET /wallet` includes the 20 most recent transactions, newest first.

**`/wallet/topup` is gated on `wallets.topup`, which ordinary users do not hold.** This is the single
most important authorization decision in the service and it is not an oversight: there is no payment
provider behind this endpoint, so it credits balance out of nothing. Granting it to users would let
anyone mint money. It stays admin-only until top-up takes real money — tracked in
`docs/money-path-defects.md`.

Validation on top-up, and why each exists:

| Rule | Reason |
|---|---|
| `> 0` | Negative would be a withdrawal through the deposit path |
| `≤ 1000` | A bound is the difference between a typo and a fortune |
| ≤ 2 decimal places | The column is `decimal(18,2)`; more would round silently |
| `PaymentMethod` non-empty | Recorded in the ledger description and the audit log |

`/wallet/ensure` is idempotent and exists because a wallet is not created at registration. The client
calls it after login so a first purchase does not fail with `WALLET_NOT_FOUND`.

---

## `/importedtickets` — ImportedTicketsController

Tickets the user already holds, brought in from a file or typed by hand. Every action is scoped to the
caller's own id taken from the JWT — there is no route that reads another user's imports.

| Method | Route | Permission | Notes |
|---|---|---|---|
| GET | `/importedtickets` | `importedtickets.view` | Paged + filtered |
| GET | `/importedtickets/{id}` | `importedtickets.view` | 404 if not yours |
| POST | `/importedtickets` | `importedtickets.create` | 201 |
| POST | `/importedtickets/upload` | `importedtickets.create` | multipart, 10 MB cap, `Upload` limiter |
| POST | `/importedtickets/extract-text` | `importedtickets.create` | `Upload` limiter |
| GET | `/importedtickets/{id}/file` | `importedtickets.view` | Streams the stored file |
| PATCH | `/importedtickets/{id}/status` | `importedtickets.manage` | |
| DELETE | `/importedtickets/{id}` | `importedtickets.manage` | Cancels; does not delete |

### List query parameters

| Parameter | Type | Default |
|---|---|---|
| `page` | `int` ≥ 1 | 1 |
| `perPage` | `int` 1–500 | 50 |
| `status` | `ImportedTicketStatus?` | all |
| `source` | `ImportSource?` | all |
| `operatorId` | `string?` | all |
| `validFrom` / `validTo` | `DateTime?` | all |
| `sort` | see below | `-createdat` |

`sort` accepts `createdat`, `validfrom`, `validto`, `ticketname`, each with a `-` prefix for
descending. An unrecognised value falls back to newest-first rather than erroring.

**The date filter is overlap, not containment**, and this was a real bug worth not reintroducing.
Filtering `ValidFrom >= from && ValidTo <= to` drops every ticket that *spans* the requested window —
exactly the ones a "valid this week" query is looking for. A ticket now matches if its validity period
intersects the range at all.

### The two-step create, and why it is two steps

Importing a file is deliberately **upload → confirm → create**, not a single call:

```
POST /importedtickets/upload   → { blobKey, fileType, extraction: { …candidates… } }
        ↓  user reviews and corrects the extracted fields in the app
POST /importedtickets          → { source, sourceFileBlobKey: blobKey, …confirmed fields… }
```

Nothing is created by the upload. What a file yields varies from near-complete (an Apple Wallet pass
is structured data) to nothing at all (a photo with no barcode). Presenting a guess as a saved ticket
would put wrong data in someone's wallet silently, so a human confirms first. The
`extraction.detectedFields` list tells the UI which values came off the file versus which are guesses.

`sourceFileBlobKey` is **required whenever `source` is not `Manual` or `Text`**. Every other source
asserts that a file backs the ticket, and `source` feeds the dedupe hash — without the requirement,
the same ticket sent twice under two different sources would evade dedupe while claiming a provenance
it does not have.

The blob key is single-use and resolved against the caller's own unconsumed uploads, so a key
belonging to someone else, an already-spent key, and a made-up string are all indistinguishable from
"not found".

| Error | Status | Cause |
|---|---|---|
| Duplicate | 409 | Matches an active ticket's dedupe hash. Retry with `allowDuplicate: true` |
| Blob unavailable | 400 | Key already used, expired, or another account's |
| Price/currency unpaired | 400 | One without the other is meaningless |
| Unsupported currency | 400 | Not in `SupportedCurrencies.All` |
| `ValidTo <= ValidFrom` | 400 | |

The 409 is recoverable by design: two passengers on the same route on the same day are a legitimate
pair of tickets, and a hard rejection left them no way through. The client is expected to show the
clash and offer `allowDuplicate`.

### Status transitions

Only three are legal, all from `Active`:

```
Active → Used
Active → Expired      (also set automatically by TicketExpiryWorker)
Active → Cancelled
```

`DELETE` cancels rather than deleting, and it enforces the same table — it used to assign `Cancelled`
unconditionally, so DELETE quietly did what PATCH forbids. Re-cancelling an already-cancelled ticket
succeeds **without a write**, because DELETE should be idempotent.

### `GET /{id}/file`

Served through the API rather than from static hosting so ownership is checked on every read and the
storage root is never exposed. `Content-Type` comes from the sniffed type recorded at upload, not from
anything the caller supplied.

---

## `/journeys` — JourneysController

Groups tickets — imported and purchased alike — into a trip.

| Method | Route | Permission | Notes |
|---|---|---|---|
| GET | `/journeys` | `journeys.view` | Paged; `status` filter |
| GET | `/journeys/{id}` | `journeys.view` | **Includes legs** |
| GET | `/journeys/suggestions` | `journeys.view` | Proposals only |
| POST | `/journeys` | `journeys.create` | 201 |
| PATCH | `/journeys/{id}` | `journeys.manage` | |
| POST | `/journeys/{id}/tickets` | `journeys.manage` | Add members |
| DELETE | `/journeys/{id}/tickets` | `journeys.manage` | Remove members (body, not route) |
| DELETE | `/journeys/{id}` | `journeys.manage` | Deletes journey, **releases tickets** |

**List omits legs, get-by-id includes them.** A list of twenty journeys would otherwise fan out into
two joins per row. `LegCount` is always present so the list can still show a count.

Ordering is upcoming-first, then undated last — `OrderByDescending(j => j.StartsAt == null)` then
`ThenBy(j => j.StartsAt)`.

**`PATCH` accepts only `Cancelled` as a status.** `Planned`/`Active`/`Completed` are a function of the
legs' dates and get recomputed by the expiry sweep, so accepting them from a client would only produce
a value that silently reverts. Cancellation is the one genuine user decision. Anything else returns
400.

**`DELETE /{id}` never deletes tickets.** The tickets are the valuable thing; the grouping is not. The
FK is `OnDelete(SetNull)` *and* the manager nulls `JourneyId` explicitly before removing the row, so
the behaviour holds even against a provider that does not apply the FK rule.

Adding tickets is all-or-nothing: if any named id is not found or not yours, the whole call 404s
rather than half-applying.

`/suggestions` returns proposed groupings over tickets not yet in a journey, each with a
human-readable `reason`. They are **never applied automatically** — a wrong guess would silently
reshuffle someone's wallet. Accepting one is a `POST /journeys` with the returned id lists. See
[domain-logic.md](domain-logic.md#journey-suggestions) for the grouping algorithm.

---

## `/api/map` — MapProxyController

| Method | Route | Query | Returns |
|---|---|---|---|
| GET | `/api/map/transport-types` | — | Raw `JsonElement` |

Gated on `map.view`. One endpoint, and it is not the client's — the **admin console** calls it as a
reachability probe, because any success means TransitInfoAPI answered and the service-account
credentials line up.

This used to be the client's only route to transit data: typed reads for stations, routes, vehicles
and departures, plus a whitelisted verbatim passthrough at `/api/map/upstream/{**path}` for the
GeoJSON the map page rendered directly. All of it existed so a page served by *this* API could reach
*that* one. The page moved to TransitInfoAPI and the client loads it from there, so it is
same-origin with its data and the proxy has no caller. See
[`docs/map-proxy-migration.md`](../../map-proxy-migration.md).

**`/upstream` is guarded by a regex allowlist**, not a blocklist. Forwarding an arbitrary path would
turn this into an open gateway to TransitInfoAPI carrying the service account's credentials, letting
any user with `map.view` reach admin endpoints there. Non-matching paths get 404
`UNKNOWN_MAP_RESOURCE` and a warning log. The allowlist is in
[transit-integration.md](transit-integration.md#the-upstream-allowlist).

Any upstream failure surfaces as **502 `TRANSIT_UPSTREAM_UNAVAILABLE`**, never a 500 — a dependency
being down is not this API being broken, and the client shows a different message for each.

---

## `/profile` and `/settings`

| Method | Route | Permission | Returns |
|---|---|---|---|
| GET | `/profile` | `profile.view` | `UserResponse` |
| PUT | `/profile` | `profile.manage` | 204 |
| GET | `/settings` | `settings.view` | `UserSettingsResponse` |
| PUT | `/settings` | `settings.manage` | 204 |

Both are self-scoped only — there is no route to read another user's profile or settings.

`GET /settings` **creates a default row** if none exists, so the client never has to handle a 404.
`PUT` treats null fields as "leave unchanged", which is why `UpdateSettingsRequest.NotificationsEnabled`
is `bool?` while the response's is `bool` — a nullable is the only way to distinguish "don't touch"
from "set false".

Note `settings.view` is in the user defaults but **`settings.manage` is not**, so an ordinary account
can read its settings but not write them through this endpoint. If settings writes are meant to be a
user-facing feature, that key needs adding to `UserRoleDefaults`.

---

## `/admin` — AdminController and RoleController

Two controllers share the `/admin` prefix. Each endpoint carries its own permission — there is no
blanket admin gate, so a custom role can be given a narrow slice.

| Method | Route | Permission |
|---|---|---|
| GET | `/admin/users?page&pageSize` | `users.view` |
| POST | `/admin/users/{userId}/role` | `users.manage` |
| GET | `/admin/stats?windowHours` | `admin.stats.view` |
| GET | `/admin/purchases?status&adapterId&page&pageSize` | `admin.purchases.view` |
| GET | `/admin/adapters?windowHours` | `adapters.view` |
| PATCH | `/admin/adapters/{adapterId}` | `adapters.manage` |
| GET | `/admin/audit?page&pageSize` | `audit.view` |
| GET | `/admin/roles` | `roles.view` |
| GET | `/admin/roles/{name}` | `roles.view` |
| POST | `/admin/roles` | `roles.manage` |
| PUT | `/admin/roles/{name}/permissions` | `roles.manage` |
| DELETE | `/admin/roles/{name}` | `roles.manage` |

`windowHours` is bounded 1–720 (30 days); `pageSize` on purchases is bounded 1–500. Unbounded values
here would let one request aggregate the entire purchase history.

`POST /admin/users/{userId}/role` **replaces** all of a user's roles rather than adding one.

`PATCH /admin/adapters/{id}` takes `{ "isActive": bool }` and only writes when the value actually
changes, so a no-op PATCH does not produce a misleading audit entry. Disabling hides an adapter's
options from the app **without touching existing tickets** — tickets already bought stay valid.

`DELETE /admin/roles/{name}` refuses to delete `Admin` or `User`, since both are recreated at startup
and are referenced by the seeding logic.

Role changes, adapter toggles and role-permission edits all write `AuditLog` rows with old and new
values.

---

## `/health`

```
GET /health   → 200 { "status": "healthy", "timestamp": "…" }
```

Anonymous. Liveness only — it does **not** check the database or TransitInfoAPI, so it answers 200
while the database is down. It is suitable as a container liveness probe, not as a readiness probe.

---

## OpenAPI

`/openapi/v1.json` and a Scalar UI are mapped **in Development only**. There is no published schema in
production; `GetThereShared` is the contract.
