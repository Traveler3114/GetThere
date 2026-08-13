# GetThere (MAUI Client) — Architecture

## What it is

A .NET MAUI application targeting **Android, iOS, MacCatalyst and Windows** from one codebase. It is
the only consumer-facing surface in the system: everything a traveller does — signing in, buying a
ticket, importing one they already hold, grouping tickets into a journey, looking at the map —
happens here.

| Property | Value |
|---|---|
| Framework | .NET 10, `net10.0-android`, `-ios`, `-maccatalyst`, `-windows10.0.19041.0` |
| Min versions | Android 21, iOS 15, macOS 15, Windows 10.0.17763 |
| App ID | `com.companyname.getthere` |
| MVVM | `CommunityToolkit.Mvvm` — source-generated observables and commands |
| UI toolkit | `CommunityToolkit.Maui` |
| Graphics | `SkiaSharp` (+ `Skottie` for Lottie animation) |
| Crash reporting | `Sentry.Maui` |
| XAML | `XamlCompilation(Compile)` — compiled, not runtime-inflated |

It talks to **GetThereAPI** for everything a user's data touches, and reads **TransitInfoAPI**
directly for one thing only: the map page, which it loads in a WebView. See
[../overview.md](../overview.md#the-one-way-rule).

---

## Structure

```
Pages/          ContentPages — XAML plus thin code-behind
ViewModels/     All presentation logic; derive from BaseViewModel
Services/       HTTP clients, device integration, and the on-device ticket store
State/          Cross-session preferences
Helpers/        ApiEndpoints, AuthenticatedHttpHandler, PageUtility + value converters
Localization/   LocalizationService, TranslateExtension, ApiMessageMapper
Shells/         AppShell (signed in), LoginShell (signed out)
Components/     Reusable views (AnimatedBackground)
Resources/      Styles, DesignSystem, Colors, Fonts, Images, .resx strings
```

### Registration by convention

`MauiProgram` registers pages and view models by reflecting over namespaces:

```csharp
// every ContentPage in GetThere.Pages
// every BaseViewModel subclass in GetThere.ViewModels
foreach (var type in …) builder.Services.AddTransient(type);
```

Adding a page or view model needs no DI edit — but, as in GetThereAPI's manager registration, **the
namespace is load-bearing**. A view model placed outside `GetThere.ViewModels`, or one not deriving
from `BaseViewModel`, is silently never registered, and the failure appears as a resolution error at
navigation time rather than at startup.

### Lifetimes, and why they are not all transient

| Lifetime | Type | Why |
|---|---|---|
| **Singleton** | `AuthService` | Holds the token cache and the refresh lock — see below |
| **Singleton** | `TicketCaptureService` | Stateless; wraps platform pickers and a Skia re-encode |
| **Singleton** | `BarcodeRenderService`, `LocalExtractionService` | Stateless; payload in, image or draft out |
| **Singleton** | `TicketStore`, `PendingImportQueue` | Each owns a file and a write lock — two screens finishing a load at once must not interleave into a half-written file. They are **not** otherwise equivalent: only `TicketStore` encrypts and scopes by owner — see [the caveat below](#the-wallet-offline) |
| **Singleton** | `CountryPreferenceService`, `IAnalyticsService` | Stateless preference access |
| **Singleton** | `AppShell`, `LoginShell` | Navigation roots |
| **Transient** | Pages, view models, API services | Fresh state per navigation |

`AuthService` being a **singleton is the correction of a real bug**, and the comment records it:

> As a transient, every consumer got its own cache (so the cache never hit) and its own refresh lock
> (so concurrent requests each rotated the refresh token, and the loser was logged out). It also
> allocates its own `HttpClient`, which must not be per-instance.

That third clause matters independently: a per-instance `HttpClient` is the classic socket-exhaustion
mistake.

---

## The HTTP stack

```
ViewModel
   ↓
XxxService  (WalletService, TicketService, ImportedTicketService, CountryService)
   ↓
IHttpClientFactory → named client "GetThereAPI"  (base address, 30 s timeout)
   ↓
AuthenticatedHttpHandler   ← attaches the bearer token, refreshes, retries
   ↓
GetThereAPI
```

`AuthService` deliberately sits **outside** this pipeline with its own bare `HttpClient`. It has to:
it is what the handler calls to refresh, so routing it through the handler would recurse infinitely.

### `AuthenticatedHttpHandler`

Two independent mechanisms keep requests authenticated.

**Pre-emptive refresh.** Before sending, the handler decodes the JWT payload and refreshes if the
token expires within 5 minutes. This avoids the round trip a 401 would cost. If the payload cannot be
read, it returns `false` — no pre-emptive refresh, fall through to the 401 path — and **logs it
rather than silently swallowing**, because a token whose expiry is unreadable is worth knowing about.

**Reactive 401 retry.** On a 401 the handler refreshes and replays the request once, guarded by
`HttpRequestOptionsKey<bool>("AlreadyRetriedAfterRefresh")` so a persistent 401 cannot loop. If the
refresh itself fails, it logs out and navigates to the login shell.

The replay is where the subtlety is. `HttpRequestMessage` **cannot be resent** — its content stream is
consumed — so the handler buffers the body to a byte array and builds a clone, copying content headers
and request headers across.

> **This is exactly why purchases must send an `Idempotency-Key`.** The handler will silently replay a
> `POST /tickets/purchase` after a token refresh. Without a key, that replay is a second charge. See
> [../getthere-api/domain-logic.md](../getthere-api/domain-logic.md#idempotency-in-detail).

### Token storage and refresh serialisation

Tokens live in `SecureStorage` (Keychain on iOS, Keystore on Android, DPAPI on Windows) under
`auth_token` and `refresh_token`, with an in-memory cache in front so hot paths avoid the platform
call.

`TryRefreshTokenAsync` is serialised by a `SemaphoreSlim`, and the reason is a direct consequence of
the server's design:

> The server rotates the refresh token and revokes the old one, so two concurrent requests presenting
> the same token would leave the second one holding a replayed token — which trips reuse detection and
> signs the user out mid-session.

The implementation uses a **read-check-recheck** pattern: capture the refresh token, take the lock,
re-read. If it changed while waiting, another caller already refreshed and stored a new token, so this
caller returns success without a request. Without that check, the queued callers would each fire a
refresh with a now-revoked token and trigger exactly the reuse detection the lock exists to avoid.

### Session state, and its three modes

| Mode | Storage | Behaviour |
|---|---|---|
| Signed in, remember me | `remember_me` preference + SecureStorage | Straight into `AppShell` on launch |
| Signed in, no remember me | SecureStorage only | Tokens exist but `App` starts at `LoginShell` |
| Guest | `is_guest` preference | `AppShell` with no token; authenticated calls fail, but the map and ticket import both work |

`App.InitializeWindowAsync` picks the root shell from this. It catches broadly and falls back to
`LoginShell` — with the comment noting `LoginShell` has no DI dependencies, so it cannot itself fail
to construct. That matters: the fallback for "something went wrong at startup" must not be able to
throw.

Shell switching is `App.GoToApp()` / `App.GoToLogin()`, both marshalled onto the main thread since
`AuthenticatedHttpHandler` can call them from a background continuation.

**Guest mode still has no server-side concept** — no anonymous token, and every authenticated call
fails. What changed is that a guest is no longer limited to browsing. **Importing a ticket works
without an account**: extraction runs on the device via `GetThereShared.Extraction`, the ticket is written to
`PendingImportQueue`, and the wallet lists it as device-only rather than showing the "account
required" scrim. `ImportSyncService` pushes the queue when the user signs in, which is the
guest-to-account upgrade — nothing did that before, so a guest who imported and then registered would
have found an empty wallet.

A pending ticket cannot be *opened*, because it has no server id and both detail screens fetch by
one. The list says so on tap rather than navigating to a screen that would fail to load.

---

## Navigation

Two shells, swapped wholesale rather than navigated between:

- **`LoginShell`** — login and registration.
- **`AppShell`** — the signed-in app.

`AppShell` builds its navigation **from a single declarative list**, adapting to the device:

```csharp
private static readonly NavItem[] Destinations =
[
    new("Tab_Profile", "profile.png", "profile_white.png", "profile",  typeof(ProfilePage)),
    new("Tab_Map",     "map.png",     "map_white.png",     "map",      typeof(MapPage)),
    new("Tab_Shop",    "shop_bag.png","shop_bag_white.png","shop",     typeof(ShopPage)),
    new("Tab_Tickets", "ticket.png",  "ticket_white.png",  "tickets",  typeof(TicketsPage))
];
```

| Idiom | Layout |
|---|---|
| Desktop | `FlyoutBehavior.Locked`, 220 px permanent side rail |
| Phone/tablet | `TabBar`, `DesktopOnly` items excluded |

The `DesktopOnly` flag exists because the design puts Settings in the desktop side rail, and the phone
frames have no room for a fifth tab — there, its contents are reached through Profile → Account.

Icons use `SetAppTheme<ImageSource>`, so light and dark themes get different assets automatically.

Three routes are registered for detail pages pushed onto the stack rather than being tabs:

```csharp
Routing.RegisterRoute("importticket",   typeof(ImportTicketPage));
Routing.RegisterRoute("ticketpurchase", typeof(TicketPurchasePage));
Routing.RegisterRoute("ticketdetail",   typeof(TicketDetailPage));
```

Parameters arrive two ways: `[QueryProperty]` for scalars (`ticketpurchase?adapterId=5`), and
`IQueryAttributable` for objects. `ImportTicketViewModel` uses the latter deliberately — the draft is
passed **as an object**, never round-tripped through a string.

### `UpdateProfileIcon` and why it looks up by route

```csharp
// Looks the item up by route rather than by position — the tree is a TabBar on phones and a
// list of FlyoutItems on desktop, so a fixed index would find the wrong item on one of them.
```

A small thing that generalises: because the shell tree differs by idiom, **any code walking it must
match on route, never on index**.

---

## Localization

Croatian and English, from `.resx` (`Resources/Strings/AppResources`), through a singleton
`LocalizationService` with an indexer that returns the key itself when a lookup fails — a missing
translation shows as `Tab_Profile` rather than blank or a crash.

`SetCulture` sets **four** culture properties, and the comment explains why:

> Setting only the calling thread leaves every other thread — and every continuation that resumes on
> the thread pool — on the old culture, so a language change appears to half-apply.

```csharp
CultureInfo.DefaultThreadCurrentCulture   = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
Thread.CurrentThread.CurrentCulture       = culture;
Thread.CurrentThread.CurrentUICulture     = culture;
```

The selection persists to the `app_language` preference; an unsupported saved value falls back to
English. `CultureChanged` lets live views re-read their strings without a restart.

`TranslateExtension` is the XAML markup extension: `{loc:Translate Tab_Profile}`.

### `ApiMessageMapper` — why server error *codes* matter

```csharp
["INVALID_CREDENTIALS"] = "Error_InvalidCredentials",
["REFRESH_TOKEN_EXPIRED"] = "Error_RefreshTokenExpired",
…
```

Server messages are English. This maps a **stable error code** to a resource key so the user sees
their own language, falling back to the server's English text when the code is unrecognised.

This is the client-side reason `AppException` carries an `ErrorCode` distinct from its message: the
code is the contract, the message is a fallback. An unmapped code degrades gracefully, so adding a
code server-side never breaks the client — it just shows English until a mapping is added.

The current map covers auth errors only. Money-path codes (`INSUFFICIENT_BALANCE`,
`CURRENCY_MISMATCH`, `ADAPTER_FAILED`) are **not** mapped and will display in English.

---

## Configuration: compile-time, and honest about it

```csharp
public static string GetThereApiBase =>
#if ANDROID
    "https://10.0.2.2:7230/";
#else
    "https://localhost:7230/";
#endif
```

`10.0.2.2` is how the Android emulator reaches the host loopback.

The doc comment is candid about the limitation:

> These were previously duplicated as literals in `MauiProgram` and `MapViewModel`… They are still
> compile-time values (a released build cannot be repointed), but there is now one place to change and
> one place to replace when the addresses move into configuration.

So this is a **known open item**: shipping to real users requires build-time configuration or a
settings-driven base URL. The consolidation into `ApiEndpoints` is the preparation for that, not the
solution.

The Sentry DSN *is* externalised — read from a bundled `appsettings.json` at startup, returning null
on any failure so a missing or malformed file disables crash reporting rather than preventing launch.
`TracesSampleRate = 0.0`: crashes only, no performance tracing.

---

## The wallet offline

The client used to persist nothing at all: no SQLite, `AppDataDirectory` never touched, every screen
a live HTTP read on appear. Offline meant an empty list and an error label — backwards for a travel
wallet, where a ticket is most needed at a barrier and a barrier is where signal is worst.

Three pieces, and the boundaries between them are the design:

| Piece | Owns |
|---|---|
| `TicketStore` | Tickets already accepted by the server. A **cache** |
| `PendingImportQueue` | Tickets created here that the server has not seen. **Not** a cache — the only copy |
| `ImportSyncService` | Draining the second into the first, on sign-in and on every wallet load |

**Network-first, cache-on-failure.** The store is written as a by-product of a read that already
succeeded, and read *only* from a failure path, so a bug in it cannot serve a stale ticket to someone
who is online. Everything is keyed by owner — the `sub` claim, or a generated guest id — because a
device is not a person, and two accounts on one phone must never see each other's tickets. The owner
key is read from the token **without checking its expiry**: it identifies whose data this is and
grants nothing, so it has to keep answering while offline with a lapsed token, which is the whole
point.

Files are AES-GCM encrypted under a key in `SecureStorage`, and the tickets directory is excluded
from Android's Auto Backup *and* device transfer. A barcode payload is a bearer credential for
travel — whoever renders it rides — so it belongs at the same protection level as the tokens.

> **This describes `TicketStore` only. `PendingImportQueue` does neither.**
>
> It holds the same `CreateImportedTicketRequest` payloads — barcode included — as plain JSON in
> `pending-imports.json`, in one file with **no owner recorded anywhere**. Two consequences follow,
> and both contradict the paragraph above:
>
> - `ImportSyncService.FlushAsync` can only gate on "someone is signed in", because no entry says
>   whose it is. A guest imports on a shared phone, a different person signs in, and the first
>   person's ticket is pushed into the second person's account.
> - It is the copy that persists **longest**: a guest never signs in, so nothing ever drains their
>   queue. The least-protected store therefore holds the credential the longest, while the encrypted
>   one holds only the copy the server already has.
>
> Recorded rather than fixed. The owner half is a product decision — a guest's entries are *meant* to
> follow the next sign-in, that being the whole guest-to-account upgrade, and nothing can tell whether
> the guest and the new account are the same person. The encryption half has no such trade-off; it
> wants `TicketStore`'s cipher lifted somewhere both classes can reach.

**Status needs care, and gets a rule of its own.** The server owns status; `TicketExpiryWorker` sweeps
hourly. A cached ticket can therefore claim `Active` long after its window shut, which at a barrier is
the one failure that matters. `TicketValidity.IsPastValidity` downgrades the *display* — never the
stored value, never anything sent to the server — and it can only ever downgrade. `Used` and
`Cancelled` come from an explicit action, possibly on another device, so recomputing them would
resurrect a cancelled ticket to active.

---

## The map: a WebView, and nothing else

`MapPage` hosts a `WebView` pointed at `{TransitInfoApiBase}map/public.html?lang=…`. That is the
whole class — a constructor and an `OnAppearing`.

It used to be considerably more. The page was served by GetThereAPI and proxied upstream, and the
search field, transport-mode chips and map controls were drawn *natively over* the WebView, driven
through four `EvaluateJavaScriptAsync` bridges. Two things fell out of moving the page to
TransitInfoAPI and the chrome into the page:

- **No token to inject.** A WebView navigation cannot carry an `Authorization` header, so the page
  used to start unauthenticated, queue its requests, and wait for `window.setAuthToken(…)` pushed in
  after navigation — deliberately not via the URL, which would have put a bearer credential in
  server logs and WebView history. The page is now same-origin with its data and reads endpoints
  that are `[AllowAnonymous]`, so there is no credential in play at all.
- **No cross-language coupling.** `MapModeChip.Key` had to stay in step with `MODE_ROUTE_TYPES` in
  the page, with no compile-time check — the one thing most likely to break silently when transport
  modes change. Its doc comment even named the wrong file. Both halves are now the page's.

What survived the move, in the page rather than in C#: chips open on Tram, and turning the last one
off restores "everything" rather than showing nothing — *an empty map is never a useful state to be
stuck in.*

The one thing the client still supplies is language. The chrome was localised from `AppResources`;
the page carries its own en/hr table and `MapViewModel` passes the current culture as `?lang=`.
`LoadMap` runs from `OnAppearing` rather than the constructor so that returning to the tab after a
language change reloads the page in it.

---

## Related documents

- [ticket-import.md](ticket-import.md) — capture, normalisation, and the draft flow
- [../getthere-api/endpoints.md](../getthere-api/endpoints.md) — the API this client consumes
- [../shared/contracts.md](../shared/contracts.md) — the DTOs it binds to
