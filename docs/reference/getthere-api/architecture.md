# GetThereAPI — Architecture and Why It Is Shaped This Way

## What this service is for

GetThereAPI is the **only** service the mobile app talks to. That is the single most important fact
about it, and most of its design follows from it.

The app needs two very different things: its own account/wallet/ticket data, and public transit
reference data (stations, routes, live vehicles). Those live in two different systems. **This API
owns the first and has nothing to do with the second.**

It used to broker transit data as well, so the client never learned that TransitInfoAPI existed.
That brokering was removed on 2026-08-02. The argument for it was:

1. **Credentials.** Reaching TransitInfoAPI's authorized surface required a service-account login. A
   client calling it directly would have to ship that credential inside an app binary, readable by
   anyone who cares to look. Keeping the call server-side kept the credential server-side.
2. **Authorization.** TransitInfoAPI has admin endpoints — feed imports, station merges,
   reconciliation. The service account could reach them, so a client holding that account could too.
   The proxy exposed a deliberately small allowlist instead.
3. **Coupling.** TransitInfoAPI's contracts could change without a client release, because only
   `TransitInfoApiClient` and `MapManager` were pinned to them.

**What dissolved the argument** is that the map page moved to TransitInfoAPI and reads that service
same-origin and *anonymously*. There is no credential to protect, no admin endpoint in reach, and no
contract pinned here — so all three reasons apply to a call that no longer happens. See
[transit-integration.md](transit-integration.md), which is kept as the historical record.

```
┌──────────────┐   HTTPS + user JWT     ┌──────────────┐                         ┌────────────────┐
│  GetThere    │ ─────────────────────► │ GetThereAPI  │     no call path        │ TransitInfoAPI │
│  (MAUI app)  │ ◄───────────────────── │              │      either way         │                │
└──────┬───────┘                        └──────┬───────┘                         └────────┬───────┘
       │                                       │                                          ▲
       │        the map — HTTPS, anonymous, same-origin with its page                      │
       ├──────────────────────────────────────────────────────────────────────────────────┘
       │                                       │                                          │
       │  references                           │ EF Core                                  │ EF Core
       ▼                                       ▼                                          ▼
┌──────────────┐                        ┌──────────────┐                         ┌────────────────┐
│GetThereShared│                        │  GetThereDb  │                         │  TransitInfoDb │
└──────────────┘                        └──────────────┘                         └────────────────┘
```

The two services never call each other in either direction. The only link between the domains is
`TicketingAdapter.TransitInfoGlobalId`, a string soft reference to an operator's Onestop ID — no
foreign key, no request.

---

## Layering, and why there is a manager layer at all

```
Controllers/   HTTP shape only — bind, authorize, delegate, translate to a status code
Managers/      All business rules and all database access
Services/      Infrastructure with a side effect or an external dependency
Sdk/           The pluggable ticketing-operator integration surface
Mapping/       Entity → contract translation
Entities/      EF Core model
Common/        Constants and pure helpers
```

The layer that earns its keep is **Managers**. Controllers in this codebase are deliberately close to
empty — most actions are four lines: pull the user id off the JWT, delegate, translate null to 404,
return. That is intentional:

- **Business rules stay testable without HTTP.** `GetThere.Tests` references internals directly
  (`InternalsVisibleTo` in the csproj) and exercises managers with no web host.
- **A rule cannot be enforced in one entry point and forgotten in another.** The ownership check on
  imported tickets lives in `ImportedTicketManager`, so `GET /importedtickets/{id}` and
  `GET /importedtickets/{id}/file` cannot disagree about who owns what.

Managers are registered **by convention**, not one by one:

```csharp
var managerTypes = typeof(Program).Assembly.GetTypes()
    .Where(t => t.Namespace == "GetThereAPI.Managers" && t is { IsClass: true, IsAbstract: false });
foreach (var mt in managerTypes)
    builder.Services.AddScoped(mt);
```

Adding a manager therefore needs no DI edit. The trade is that the namespace is load-bearing — a
class placed in `GetThereAPI.Managers` becomes an injectable scoped service whether or not that was
intended.

---

## The authorization model, and why it is claims-based rather than role-based

Roles alone would have been simpler. The system uses **permissions carried as claims**, with roles as
nothing more than a bundle of them, because permissions need to change without a code deploy: an
admin can create a role and assign it permissions through `/admin/roles` at runtime.

Three pieces make this work.

### 1. `PermissionKeys` — the vocabulary

Every permission is a string constant like `tickets.view` or `journeys.manage`, all of them listed in
`PermissionKeys.All`. That list is what `Program.cs` iterates to register one authorization policy per
permission:

```csharp
foreach (var perm in PermissionKeys.All)
    options.AddPolicy(perm, p => p.RequireAssertion(ctx =>
        ctx.User.IsInRole(RoleNames.Admin) || ctx.User.HasClaim("permission", perm)));
```

Two consequences worth knowing:

- **Admin bypasses every permission check.** The assertion short-circuits on the Admin role, so an
  admin never needs a permission granted explicitly.
- **A permission not in `All` has no policy**, so `[Authorize(Policy = "…")]` naming it fails closed
  at startup rather than silently allowing everything.

`PermissionKeys.UserRoleDefaults` is the subset granted to every registered account at startup. The
file carries a warning that matters:

> Anything listed here is held by every registered account, so an endpoint gated on one of these is
> effectively public to authenticated users — never use one to guard platform-wide or cross-user data.

That is why `admin.stats.view` and `admin.purchases.view` exist as separate keys rather than reusing
`tickets.view`: those endpoints expose every user's purchases and the platform's financial totals.

It is also why **`wallets.topup` is deliberately excluded from the user defaults**. Top-up credits a
balance with no payment provider behind it — it mints money. Until a provider is wired in, only an
admin can call it. `wallets.manage`, which users *do* hold, does not cover it.

### 2. `DynamicClaimsTransformation` — keeping claims fresh

The JWT carries role claims from the moment it was issued. If permissions were read from the token,
revoking someone's access would not take effect until their token expired — up to an hour.

So on every authenticated request this transformation **strips the token's `role` and `permission`
claims and reloads them from the database**. Revocation then takes effect within seconds rather than
a token lifetime.

That would be a database round-trip per request, so results are cached per user with two expirations:

| Setting | Value | Why both |
|---|---|---|
| `SlidingExpiration` | 30s | Collapses bursts of requests from an active client |
| `AbsoluteExpirationRelativeToNow` | 5 min | Sliding alone **never lapses** for a user who keeps making requests, which would let a revoked role stay live indefinitely |

The absolute ceiling is the real security control; the sliding window is only an optimisation.

### 3. The shared `IMemoryCache` and its size limit

`Program.cs` registers one memory cache with `SizeLimit = 2_000`. The limit was introduced for
`MapManager`, whose map reads were keyed by viewport — a user panning around produces an unbounded
set of distinct keys, so an unbounded cache is a slow memory leak. `MapManager` is gone and this
transformation is now the only consumer, but the limit stays: the reasoning applies to any future
cached read keyed by user-supplied input, and removing a bound is harder to notice than keeping one.

Because a size limit is set, **every entry must declare a `Size`**. Entries use `Size = 1`, so
the limit is effectively an entry count. An entry added without a size throws at runtime; this is the
most common way to break the cache when adding a new cached read.

---

## Authentication: why refresh tokens are shaped the way they are

Access tokens are JWTs, signed HS256, default 60-minute lifetime. Refresh tokens are 64 random bytes,
base64-encoded. Three decisions in `AuthManager` and `TokenManager` are worth understanding.

### Refresh tokens are stored hashed

The `RefreshTokens.Token` column holds a **base64 SHA-256 hash**, never the token itself. A database
leak therefore does not yield usable credentials.

There is a subtle bug fixed here that is worth preserving: the column was originally unbounded, which
makes it `nvarchar(max)` in SQL Server — and SQL Server **cannot index `nvarchar(max)`**. The declared
unique index was silently never created, so every refresh table-scanned. It is now `HasMaxLength(128)`
(a base64 SHA-256 is 44 chars) with a real unique index. Uniqueness is not cosmetic: the hash is the
lookup key, and two rows sharing one would make rotation and reuse detection ambiguous.

### Rotation with reuse detection

Every refresh **rotates**: the presented token is revoked and a new one issued. The old row records
`ReplacedByToken`.

If a token that was already replaced comes back, that means someone is replaying a stolen token — so
**every active token for that user is revoked**, forcing a full re-login.

The ordering here is load-bearing, and the code says so:

```csharp
// This must run *before* the IsActive guard: rotation sets both RevokedAt and ReplacedByToken,
// so a replayed rotated token is already inactive and would otherwise never reach this branch.
```

Check reuse first, *then* check active. Reversed, reuse detection would be dead code.

**The rotation itself is a conditional update, not a read-then-write** (2026-08-10). The revoke is
issued as a single statement whose `WHERE` re-asserts the precondition:

```csharp
var claimed = await _db.RefreshTokens
    .Where(rt => rt.Id == existingRefreshToken.Id
        && rt.RevokedAt == null
        && rt.ReplacedByToken == null)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(rt => rt.RevokedAt, rotatedAt)
        .SetProperty(rt => rt.ReplacedByToken, newHashedRefreshToken), ct);
```

`claimed == 0` means someone else rotated this exact token between the read above and this write.
That is the same event as presenting an already-rotated token, so it gets the same answer — revoke
the whole family. Treating the race as benign would mean a stolen token that beats the real client
to the server is rewarded with a working session.

Without this, two concurrent refreshes both pass the reuse check, both write, and both succeed:
reuse detection is intact for a *replay*, and blind to a *race*. TransitInfoAPI's `AuthManager`
carries the identical rotation, for the same reason.

### The address is recorded, not enforced

A refresh used to be **rejected** when the token was issued with an IP address and the request
presented a different one. That was removed on 2026-07-31; the address is now audited as
`RefreshAddressChanged` and nothing more.

The cost was not theoretical. `Invalid` becomes a 401, and the MAUI client answers a failed refresh by
clearing its credentials and returning to the login screen — so the check fired on every
wifi-to-cellular handover, cell handover, CGNAT rebinding and IPv6 privacy-extension rotation. For a
travel app whose users are by definition moving, that is a sign-out several times a day, and it takes
any offline-cached ticket with it.

What it bought in exchange was small:

- **Rotation and reuse detection already catch a stolen token** regardless of where it is presented
  from. That is the primary control and it is untouched.
- **It was bypassable by the adversary it targeted.** `UseForwardedHeaders()` runs with
  `KnownIPNetworks` and `KnownProxies` cleared, so `X-Forwarded-For` is honoured from any immediate
  peer — someone holding a stolen token could simply assert the address the check wanted.
- **It could not tell "user moved" from "thief"**, so there was no stricter or looser version that
  would have been better.

The control that would earn its place is a binding to the **device** rather than the network: a
client-generated identifier that survives a change of network but not a change of hardware. Note
`DeviceInfo` is *not* that — it is the raw `User-Agent`, caller-supplied and not unique.

`RefreshTokenEvaluator.IsAddressChange` still exists, deliberately separate from `Evaluate`, so the
split between "decides the verdict" and "worth recording" is visible in the type.

### Registration does not confirm whether an address exists

`RegisterAsync` returns success whether or not the email is already taken. Answering "email already in
use" turns registration into an oracle for whether an address has an account here.

Two details make the disguise hold:
- On the duplicate path the code still runs `PasswordHasher.HashPassword` on a throwaway user, so the
  response does not come back measurably faster.
- Identity also reports duplicates as a validation error, and that error is collapsed into the same
  silent success — otherwise the race between the check and the create would be an oracle too.

`LoginAsync` uses the same trick: an unknown address still pays for a password hash.

The cost is that a user who genuinely forgot they had an account gets no feedback. The intended fix
(a "someone tried to register with your address" email) is a TODO in the code, blocked on there being
no mail sender.

---

## The error model

One exception type, `AppException(message, statusCode = 400, errorCode = null)`, is the only way to
produce a non-500. The global handler in `Program.cs` turns it into RFC 9457 problem JSON:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1", "title": "…", "status": 400 }
```

The `title` is `ErrorCode ?? Message`. **Anything that is not an `AppException` becomes a bare 500
with the title "Internal Server Error"** — the real message is logged, never returned, so an
unexpected failure cannot leak internals. In Development only, the exception type and message are
included in the title instead, because otherwise debugging means reading server logs for every typo.

The client reads the `title` field back out via `HttpHelper.TryReadProblemAsync` and runs it through
`ApiMessageMapper` for localization, which is why error *codes* (`INVALID_CREDENTIALS`,
`INSUFFICIENT_BALANCE`, `CURRENCY_MISMATCH`) matter more than error *messages* — the code is the
stable contract, the message is a fallback.

---

## Rate limiting and the proxy problem

Three limiters, partitioned by IP:

| Limiter | Limit | Applied to | Why |
|---|---|---|---|
| Global | 100/min | Everything | Baseline |
| `Auth` | 10/min | `AuthController` | Login and refresh are credential-guessing targets |
| `Upload` | 10/min | `/importedtickets/upload`, `/extract-text` | Each upload can carry 10 MB and costs image decoding, barcode scanning and PDF parsing — the global allowance is far too loose for that |

`QueueLimit = 0` everywhere: over the limit is a 429 immediately, not a queued request holding a
connection.

The partition key is `context.Connection.RemoteIpAddress`. **Behind a reverse proxy that is the
proxy's address, so every caller in the world would share one partition** — the limiter would be
worse than useless. Hence `UseForwardedHeaders()` with `X-Forwarded-For`.

`KnownIPNetworks` and `KnownProxies` are both cleared, because the proxy address is not known at build
time. That is a real trade-off and the code flags it: **only deploy this way where a trusted proxy
terminates TLS**, otherwise a client can spoof `X-Forwarded-For` and mint itself a fresh rate-limit
partition per request.

---

## Static file hosting: the admin console and the map

Two static surfaces are served from `wwwroot`, and both have unusual configuration for stated reasons.

### `/admin` has no authorization gate — on purpose

The comment in `Program.cs` explains it:

> Authentication here is bearer-token based, and a browser navigation to an `.html` file cannot send
> an `Authorization` header — a gate on these paths 401s the login page itself and makes the console
> unreachable.

The console holds no secrets. Every byte of data it renders comes from API endpoints that *are*
authorized per-endpoint. Serving the HTML shell publicly leaks nothing; serving the data does, and
that is gated.

It is still hardened as a backstop, because the console renders operator-supplied text and a CSP is
what catches an escaping bug:

- `Content-Security-Policy` with `frame-ancestors 'none'`, `default-src 'self'`
- `X-Robots-Tag: noindex, nofollow`
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`

`'unsafe-inline'` is currently required because the pages carry inline `<script>` and style blocks;
the comment marks it for removal once those move to files.

### `/map` gets permissive CORS, scoped

```csharp
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/map"),
    branch => branch.UseCors("MapAssets"));
```

The `MapAssets` policy is `AllowAnyOrigin/Method/Header` — deliberately wide open, so map assets can
be embedded cross-origin. It is applied through `UseWhen` on the `/map` path rather than globally
**so it never widens access to the authenticated API surface**. Moving this to a global `UseCors`
would be a serious regression.

---

## Startup seeding, and the one place it refuses to be convenient

At startup the app ensures the `Admin` and `User` roles exist, grants Admin every permission in
`PermissionKeys.All`, grants User everything in `UserRoleDefaults`, and seeds `admin@getthere.local`.

The admin password is where it gets deliberately awkward:

- **In Development**, if no password is configured, one is generated and written to
  `.admin-credentials` next to the binary, with the path printed to the console. The generated
  password has to reach the developer somehow.
- **Outside Development**, if `Seed:AdminPassword` is not configured, the seed is **skipped with a
  warning** rather than generating one.

The reason is stated in the code: generating a password and dropping it on disk in plaintext leaves a
credential at rest on every deployment. Refusing to create the account is the safer failure.

Note the seeding is claim-additive — it adds permissions the role is missing but never removes ones it
has. Deleting a key from `PermissionKeys.All` therefore leaves the stale claim in the database.

---

## Configuration

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | **Yes** | Startup throws without it |
| `Jwt:Key` | **Yes** | Must be ≥32 bytes and not the literal `CHANGE-ME`; startup throws otherwise |
| `Jwt:Issuer` / `Jwt:Audience` | Yes | Both validated on every token |
| `Jwt:ExpiryMinutes` | No | Default 60 |
| `Jwt:RefreshTokenDays` | No | Default 1 |
| `Jwt:RefreshTokenDaysRememberMe` | No | Default 30 |
| `TicketFiles:RootPath` | No | Defaults to `{ContentRoot}/ticket-files` |
| `TicketExpiry:CheckIntervalHours` | No | Default 1, floored at 1 minute |
| `Seed:AdminPassword` | Outside Dev | See above |

The two hard startup failures are intentional — a missing connection string or a weak JWT key are
conditions where refusing to start beats starting insecurely. `UserSecretsId` is set in the csproj, so
local development uses `dotnet user-secrets` rather than a checked-in file.

**"Remember me" survives rotation.** `TokenManager.IsRememberMeRefreshToken` infers it by comparing
the old token's lifespan against the configured standard, rather than storing a flag — so a rotated
30-day token stays a 30-day token. Change `Jwt:RefreshTokenDays` and existing tokens get re-classified
on their next refresh, which is the known cost of inferring rather than storing.

---

## Related documents

- [endpoints.md](endpoints.md) — every route, its policy, and why it is gated that way
- [domain-logic.md](domain-logic.md) — the money path, imports, journeys
- [ticket-import.md](ticket-import.md) — the file upload and extraction pipeline
- [transit-integration.md](transit-integration.md) — **historical**: the TransitInfoAPI client and the
  service-account hop, both removed. Kept for the allowlist and 502-not-500 reasoning
- [../shared/contracts.md](../shared/contracts.md) — the DTOs crossing the wire
