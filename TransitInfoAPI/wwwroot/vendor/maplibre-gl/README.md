# MapLibre GL JS — vendored

**Version 4.7.1.** BSD 3-Clause; see `LICENSE.txt`, kept alongside the code because the licence
requires the notice to travel with it.

These files were loaded from `https://unpkg.com/maplibre-gl@4.7.1/dist/...` by all four map pages.
That made a public CDN a hard runtime dependency of the map: unreachable CDN, blank page. It matters
more than it used to, because the map's own chrome now lives in `map/public.js` — a script that
never runs if `maplibregl` is undefined, so a CDN failure took the search box and mode chips with the
map itself.

## Updating

```bash
npm pack maplibre-gl@<version>
tar xzf maplibre-gl-<version>.tgz package/dist/maplibre-gl.js package/dist/maplibre-gl.css package/LICENSE.txt
cp package/dist/maplibre-gl.{js,css} package/LICENSE.txt TransitInfoAPI/wwwroot/vendor/maplibre-gl/
```

`dist/maplibre-gl.js` is the production build — `maplibre-gl-dev.js` is the unminified one, and the
`.js.map` is deliberately not vendored. Bump the version here and in this file's heading together.

## What this does *not* make offline

The basemap still comes from `https://tiles.openfreemap.org` — tiles, glyphs and sprites, all named
in `map/style.json` and allowed by the CSP in `Program.cs`. Vendoring the library removes the CDN
that serves the *code*; the map still needs network for its *data*. Genuine offline use means
self-hosting or packaging tiles, which is a much larger piece of work.
