# Map proxy migration (H5) — outstanding

**Status:** not done. The violation is isolated but still live.

## What is wrong

`AGENTS.md` states a one-way rule: the MAUI client talks only to GetThereAPI, and
`MapProxyController` exists to serve exactly that purpose. The map does not follow it.

`GetThere/Helpers/ApiEndpoints.MapPageUrl` loads `map/public.html` from GetThereAPI but passes
TransitInfoAPI's address as an `api` query parameter. `public.html:60` reads it:

```js
const API_BASE = new URLSearchParams(window.location.search).get('api') || '';
```

and then fetches TransitInfoAPI directly — `/stations`, `/routes`, `/mobility/stations`,
`/realtime/vehicles`, `/stations/{id}/departures` (lines 100–127, 424). The MAUI client therefore
depends on both platforms, and `MapProxyController` is dead weight for this path.

`audit.md` lists this as critical #7 and marks it fixed. It is not.

## Why it was not fixed in this pass

Two things block a one-line redirect, and neither is small:

1. **Response shape.** The page requests `?format=geojson` and renders GeoJSON feature collections.
   `MapProxyController` returns `MapStationResponse` / `MapRouteResponse` lists — a different shape
   entirely. Pointing the page at the proxy without changing anything renders an empty map.
2. **Authentication.** The TransitInfoAPI endpoints the page calls are `[Authorize]`-gated, and so is
   `MapProxyController` (`PermissionKeys.MapView`). A WebView navigation carries no bearer token, so
   the page currently only works to the extent those calls succeed unauthenticated — of the endpoints
   it uses, only `/mobility/stations` is `[AllowAnonymous]`.

Redirecting the page at the proxy without doing both would have replaced a boundary violation with a
blank screen, so the current behaviour was left intact and the addresses were consolidated into
`ApiEndpoints` instead.

## What the fix requires

1. **GeoJSON passthrough on the proxy.** Add `format=geojson` support to `MapProxyController` for
   stations, routes and mobility stations. The cleanest form is a raw passthrough: a method on
   `TransitInfoApiClient` that returns the upstream JSON body unparsed, so GetThereAPI does not have
   to model GeoJSON. Add `/map/vehicles` and `/map/departures/{onestopId}` to match the page's needs.
2. **Token delivery to the WebView.** Do *not* put the access token in the page URL — it lands in
   server request logs and WebView history. Instead let the page start unauthenticated and have
   `MapPage` push the token in after navigation:

   ```csharp
   await MapWebView.EvaluateJavaScriptAsync($"window.setAuthToken('{token}')");
   ```

   `public.html` then holds the token in a module-scoped variable and attaches
   `Authorization: Bearer …` to every fetch, deferring its first load until the token arrives.
3. **Drop the `api` parameter** so `API_BASE` falls back to the page's own origin, and delete
   `ApiEndpoints.TransitInfoApiBase`.
4. **Token refresh.** The access token is valid for 15 minutes; a map left open outlives it. Either
   re-inject on 401 or have the page call back out to MAUI to request a fresh token.

## Verification

Run the MAUI app with TransitInfoAPI **stopped**. The map must still render stations, routes and
vehicles through GetThereAPI. Today it renders nothing.
