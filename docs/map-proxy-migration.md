# Map proxy migration (H5)

> **SUPERSEDED, 2026-07-31.** The proxy this describes no longer exists. The map page moved to
> TransitInfoAPI and the client loads it from there, which makes the page same-origin with the data
> it reads — so the passthrough, the allow-list, the service-account hop and the token injection
> below were all deleted rather than maintained. They existed only to bridge two origins.
>
> The one-way rule was amended to say so: the client uses GetThereAPI for all business data and
> reads TransitInfoAPI for the map alone. See `AGENTS.md`.
>
> Kept because it explains *why* the proxy was built, which is what makes the deletion legible —
> and because the allow-list reasoning still applies to anything that proxies upstream in future.

**Status: DONE, 2026-07-27.** Verified end-to-end against both APIs running.

## What was wrong

`AGENTS.md` states a one-way rule: the MAUI client talks only to GetThereAPI. The map did not follow
it. `ApiEndpoints.MapPageUrl` loaded `map/public.html` from GetThereAPI but passed TransitInfoAPI's
address as an `?api=` parameter, and the page then fetched TransitInfoAPI directly. The client
therefore depended on both platforms, and `MapProxyController` was dead weight for this path.

`audit.md` listed this as critical #7 and marked it fixed. It was not.

## What was done

**A whitelisted verbatim passthrough**, `GET /api/map/upstream/{**path}`
(`MapProxyController.GetUpstream`). The map page renders upstream shapes directly — GeoJSON feature
collections in particular — so re-modelling them in GetThereAPI would only have added drift. The
proxy forwards the path and query string unchanged and returns the body untouched.

The allowlist (`MapManager.IsAllowedUpstreamPath`) is the security control, and it is not optional:
the proxy authenticates upstream with the **service account**, so forwarding an arbitrary path would
let any user holding `map.view` reach TransitInfoAPI's admin endpoints. Only these are permitted:

```
stations              routes                mobility/stations
realtime/vehicles     realtime/alerts       map/transport-types
stations/{id}/departures                    stations/{id}/operators
```

**Token delivery without putting it in the URL.** The page starts unauthenticated and queues its
requests behind a promise; `MapPage.OnMapNavigated` calls
`window.setAuthToken(...)` through `EvaluateJavaScriptAsync` once navigation completes. A token in
the query string would land in server request logs and WebView history. Browsing the page directly
in a browser falls back to the `auth_token` already in `sessionStorage`.

**Three stubbed proxy endpoints were implemented** while doing this. `GetDeparturesAsync`,
`GetStationOperatorsAsync` and `GetTransportTypesAsync` returned hardcoded `[]` with a log warning
(`audit.md` high #5); they now call upstream.

**Upstream failures return 502, not 500.** A connection failure or timeout to TransitInfoAPI was
surfacing as an unhandled `HttpRequestException`.

`ApiEndpoints.TransitInfoApiBase` is gone — the MAUI client no longer knows TransitInfoAPI's address.

## Verified

With both APIs running against real data:

| Check | Result |
|---|---|
| `/api/map/upstream/stations?format=geojson&...` | 200, 133 KB `FeatureCollection` |
| `/api/map/upstream/realtime/vehicles` | 200, 114 KB live vehicle array |
| `/api/map/upstream/mobility/stations?format=geojson&...` | 200, 23 KB `FeatureCollection` |
| `operators`, `feeds`, `users`, `reconciliation/candidates`, `agencies` | 404 — never forwarded |
| `../auth/login`, `stations/1/../../users` | 404 — traversal does not escape the allowlist |
| No bearer token | 401 |
| `/map/public.html` | 200, and references `/api/map/upstream` only |

## Note for whoever runs this locally

GetThereAPI authenticates to TransitInfoAPI as `getthere-api`. In Development TransitInfoAPI seeds
that account with a **randomly generated** password written to
`TransitInfoAPI/bin/Debug/net10.0/.service-account-credentials`, so it will not match whatever
`TransitInfoApi:ClientSecret` GetThereAPI already has — the symptom is a 502 from every map call.
Either copy the generated password into GetThereAPI's user secrets:

```bash
dotnet user-secrets set "TransitInfoApi:ClientSecret" "<password from .service-account-credentials>" --project GetThereAPI/GetThereAPI.csproj
```

or set `Seed:ServiceAccountPassword` on TransitInfoAPI to a known value before the account is first
created, which is what non-Development environments now require.
