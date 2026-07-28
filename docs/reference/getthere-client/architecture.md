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

It talks to **GetThereAPI only**. It has no knowledge of TransitInfoAPI, by design — see
[../getthere-api/transit-integration.md](../getthere-api/transit-integration.md#the-one-way-rule).

---

## Structure

```
Pages/          ContentPages — XAML plus thin code-behind
ViewModels/     All presentation logic; derive from BaseViewModel
Services/       HTTP clients and device integration
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
| Guest | `is_guest` preference | `AppShell` with no token; authenticated calls fail |

`App.InitializeWindowAsync` picks the root shell from this. It catches broadly and falls back to
`LoginShell` — with the comment noting `LoginShell` has no DI dependencies, so it cannot itself fail
to construct. That matters: the fallback for "something went wrong at startup" must not be able to
throw.

Shell switching is `App.GoToApp()` / `App.GoToLogin()`, both marshalled onto the main thread since
`AuthenticatedHttpHandler` can call them from a background continuation.

**Guest mode has no server-side concept.** It is purely a client preference that lets someone browse
the map without an account; every authenticated call simply fails. There is no anonymous token.

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

## The map: a WebView, and how the token reaches it

`MapPage` hosts a `WebView` pointed at `{GetThereApiBase}map/public.html` — a page served by
GetThereAPI that calls GetThereAPI's own map proxy on the same origin.

The interesting problem is authentication. **A WebView navigation cannot carry an `Authorization`
header.** The options were to put the token in the URL or to inject it after load; the code chooses
injection:

```csharp
await MapWebView.EvaluateJavaScriptAsync($"window.setAuthToken && window.setAuthToken('{escaped}')");
```

> Pushing the token in afterwards keeps it out of the URL, where it would otherwise end up in server
> request logs and WebView history.

The page holds its requests until `setAuthToken` is called. Two details:

- The token is **escaped** before interpolation. It is base64url and carries no quotes, but the code
  escapes defensively rather than relying on that.
- The mode filter is passed through `JsonSerializer.Serialize` rather than string-concatenated —
  "the keys are ours today, but a hand-built array literal is the kind of thing that quietly becomes
  an injection point later."

Native chips are drawn *over* the WebView, so `MapViewModel` raises a `ModeFilterChanged` event and
the page pushes it in. The view model holds **no WebView reference** — it reports, the page calls.
That is what keeps it testable. The filter is replayed once after navigation completes, because the
page starts out showing everything.

`MapModeChip.Key` must stay in step with `MODE_ROUTE_TYPES` in `public.html` — a **cross-language
coupling with no compile-time check**, and the one thing most likely to break silently when transport
modes change.

Turning the last chip off restores "everything" rather than showing nothing: *an empty map is never a
useful state to be stuck in.*

---

## Related documents

- [ticket-import.md](ticket-import.md) — capture, normalisation, and the draft flow
- [../getthere-api/endpoints.md](../getthere-api/endpoints.md) — the API this client consumes
- [../shared/contracts.md](../shared/contracts.md) — the DTOs it binds to
