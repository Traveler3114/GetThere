# Verification checklist — `claude/maui-ui-to-transitinfo-map-9cj09m`

**Disposable.** This exists to get one branch verified. Delete it once the branch is merged — it is
not part of the permanent docs.

## Read this first

**It was written in a container with no .NET SDK**, so nothing here was compiled locally. CI has
since compiled and run it, which changes what is worth your time — see below. Every claim about
*behaviour* is still derived from reading the code, not from observing it.

**What CI has now proven** (`.github/workflows/build-check.yml` on PR #72):

- All five non-MAUI projects build with `-warnaserror`, and `dotnet format --verify-no-changes`
  passes on each.
- **301 tests pass**, against a real SQL Server. That includes the hand-written migration below,
  which all three database fixtures apply via `Database.Migrate()`.
- The MAUI project's **C# compiles for `net10.0-android`**.

**What CI still does not cover**, and where the remaining risk sits:

- **XAML compilation.** The Android build failed at `CoreCompile` on its first pass, so `XamlC` and
  everything after it has run only once, at the end. The ticket card's `DataTemplate` is untyped, so
  a wrong property name fails at *runtime* regardless — item 4 below stands.
- **iOS, MacCatalyst and Windows heads** are never built by any job.
- **`dotnet format` is not run against the MAUI project.**
- Anything requiring a device, a screen or a scanner — which is most of §3.

Static checks that *were* done by hand, so you can skip re-deriving them: no references remain to any
deleted or moved symbol; `.resx` parity holds at 281 keys in both cultures; every `.csproj`, `.xaml`,
`.slnx` and Android manifest parses; every changed constructor has a matching DI registration.

Twelve commits, in two groups that can be reviewed together:

| Commits | Theme |
|---|---|
| `0b54a3b` → `7900d2e` | The map: UI into the page, page served by TransitInfoAPI, proxy retired, MapLibre vendored |
| `0bea57b` → `4318c52` | Tickets: offline wallet, scannable codes, guest import, refresh-token change |

---

## 1. Build and test

CI (`.github/workflows/build-check.yml`) does all of this on a PR and is the authoritative run.
Locally:

```bash
dotnet build GetThere.slnx
dotnet test tests/GetThere.Tests/GetThere.Tests.csproj
```

Then, because CI does **not** cover them:

```bash
# dotnet format is not run against the MAUI project in CI, and iOS/Windows heads are never built
dotnet build GetThere/GetThere.csproj -f net10.0-android -warnaserror
dotnet build GetThere/GetThere.csproj -f net10.0-ios          # macOS only
dotnet build GetThere/GetThere.csproj -f net10.0-windows10.0.19041.0   # Windows only
```

**Most likely to be red, in order.** Items 1–3 are now settled by CI and are here only so you do not
re-investigate them; item 4 is the live one.

1. ~~**`GetThereExtraction` is a new project.**~~ Builds in CI. It was added to `GetThere.slnx`, both
   apps, the test project, and all four places `build-check.yml` names projects individually — but
   CI builds projects *individually*, never the solution, so a bad `.slnx` entry would still only
   show up in a local `dotnet build GetThere.slnx`. That is the one part of this item left to check.
2. ~~**`BarcodeRenderService` ZXing APIs**~~ — written from memory against ZXing.Net 0.16.11 and, as
   expected, wrong: `PixelData` is in `ZXing.Rendering`, not `ZXing`. Fixed; the file now compiles,
   which means `BarcodeWriterPixelData`, `QrCodeEncodingOptions`, `EncodingOptions.PureBarcode` and
   `ErrorCorrectionLevel.Q` all resolve. **Compiling is not scanning** — §3b is still the real test.
3. ~~**`TicketStore`'s `AesGcm`** and `SKBitmap.InstallPixels` over a pinned buffer~~ — compile.
   Whether the pinned-buffer copy produces a *correct* image is §3b; whether the ciphertext
   round-trips is §3c item 17.
4. **XAML bindings** — `TicketsPage` and the two detail pages changed shape. The ticket card's
   `DataTemplate` is untyped, so a wrong property name fails at runtime, not compile time. **This is
   the one CI cannot reach**, and the reason §3c item 1 is worth doing first.

## 2. The database change — resolved, 2026-08-02

`20260731120000_AddImportedTicketClientId.cs` and the matching `AppDbContextModelSnapshot.cs` edit
were written **by hand**, as an explicitly granted exception to `AGENTS.md`'s "never manually edit
`*ModelSnapshot.cs`". **That migration never ran anywhere**, and the failure was silent.

It had no `[Migration]` attribute and no `.Designer.cs`. The attribute is how EF discovers a
migration, so the class was invisible: absent from `dotnet ef migrations list`, and skipped without
comment by `dotnet ef database update`, which still reported `Done.` The column never reached the
database while the EF model expected it, so **every query touching `ImportedTickets` failed with
`Invalid column name 'ClientId'`** — the tickets screen and the journeys list, which
`.Include(j => j.ImportedTickets)`.

It has been re-scaffolded as `20260802130743_AddImportedTicketClientId` and applied. The DDL the tool
produced is identical to the hand-written version: the SQL was always right, and only the metadata
that makes it *run* was missing.

```bash
# Should report no pending model changes.
cd GetThereAPI && dotnet ef migrations has-pending-model-changes
```

> **The lesson is worth keeping even though the file is gone.** The tests did not catch this, and
> could not: they build their schema from the model, so a migration that never executes is
> indistinguishable from one that does. Only a query against a real database shows the drift — which
> is why this section exists at all.

---

## 3. Manual passes

### 3a. Map

Run **TransitInfoAPI with the `https` profile** — this is a change, and the old `http` profile will
fail silently on Android because the manifest sets `usesCleartextTraffic="false"`.

```bash
dotnet run --project TransitInfoAPI/TransitInfoAPI.csproj --launch-profile https   # :5001
dotnet run --project GetThereAPI/GetThereAPI.csproj --launch-profile https          # :7230
```

| # | Check | Expected |
|---|---|---|
| 1 | `https://localhost:5001/map/public.html` **logged out entirely** | Stations, routes, bike docks, live vehicles and search all load. Any 401 means an endpoint is still gated |
| 2 | DevTools console on that page | **No CSP violation.** The map's policy is now `script-src 'self'` with no CDN |
| 3 | Network tab | The only off-origin request is `tiles.openfreemap.org` |
| 4 | `?lang=hr` | Croatian labels |
| 5 | Chips: toggle each | Stops/routes/vehicles filter; Bikes toggles the orange docks; turning the last chip off restores everything |
| 6 | Search a station | Dropdown appears, picking one flies the map in and opens the sidebar with departures |
| 7 | Recentre / layers buttons | Prompts for location and moves; hides/shows route lines |
| 8 | Unclustered stops | Show bus/tram/train icons |
| 9 | **Android emulator specifically** | The map loads at all. This is the single most likely thing to be broken and will not reproduce on Windows |
| 10 | GetThereAPI admin console | The rail foot shows **no** TransitInfoAPI status dot. The probe and the endpoint behind it are gone; the rail keeps only the "one-way · GlobalId reference" line |
| 11 | `/api/map/upstream/stations` and `/api/map/transport-types` | 404 from routing. The whole `/api/map` surface is gone, not just the passthrough |
| 12 | GetThereAPI started with **TransitInfoAPI stopped** | Boots clean, no upstream warnings. Admin console, wallets, tickets, imports and journeys all work — there is no longer any code path between the two services |

> The map page was exercised in a headless browser with stubbed endpoints, so the chrome, chip logic,
> search and i18n are known to work. What is **not** verified is any of it against real GeoJSON.

### 3b. Tickets — the barcode is the risk

**This is the check that matters most on the whole branch**, and no test can cover it.

| # | Check | Expected |
|---|---|---|
| 1 | Buy a ticket → detail opens → **scan the code with a second phone** | Decoded text matches `TicketResponse.Data` exactly |
| 2 | Repeat for an imported ticket with a `RawPayload` | Same |
| 3 | An Aztec or PDF417 **rail** ticket | Falls back to **text**, does not render a code. This is deliberate — see below |
| 4 | A ticket with no payload | "No scannable code on this ticket" |

Why #3: `TicketFormat` has five values, and `BarcodeDecoder.ToTicketFormat` collapses everything that
is not QR or DataMatrix into `Barcode`. So a UIC 918-3 rail payload is indistinguishable from a short
Code 128 one, and re-encoding it would produce a symbol that scans to the wrong bytes.
`TicketBarcode.ChooseSymbology` returns null rather than guessing. **A code that renders but does not
scan is the failure mode to hunt for.**

### 3c. Tickets — wallet, offline, guest

| # | Check | Expected |
|---|---|---|
| 1 | Open the Tickets tab signed in | **Purchased tickets now appear**, merged with imported ones, newest first |
| 2 | Tap a card | Opens the ticket. It used to raise an action sheet |
| 3 | Cancel/mark-used | On the `⋯` control, and only on imported active tickets |
| 4 | Airplane mode, force-quit, relaunch | App opens, list renders from cache, ticket opens, code still scans |
| 5 | Same, with an access token older than 15 min | Offline state — **not** a crash, and **not** the "account required" wall. This was the slice-0 bug |
| 6 | Cache banner | "Saved 3 h ago · showing your last update" |
| 7 | Ticket whose `ValidTo` passes while offline | Presents as expired, not Active |
| 8 | Ticket with a **null** `ValidTo` | Does **not** expire |
| 9 | Cancel on device A while B is offline | B never resurrects it to Active |
| 10 | Sign in as a second user on the same device | None of the first user's tickets appear |
| 11 | **Continue as guest → import a ticket by photo** | Works with no account. Listed as device-only |
| 12 | Guest → tap that pending ticket | Says it is not saved to an account yet; does not navigate |
| 13 | Guest → register or sign in | The ticket appears on the server, exactly once |
| 14 | Run the flush twice | Still exactly once — the `ClientId` index |
| 15 | Import a **PDF** as a guest | Should say an account is required, not fail obscurely |
| 16 | Explicit sign-out | Cache cleared |
| 17 | Rooted emulator: inspect `AppDataDirectory/tickets` | No plaintext payload |

### 3d. Auth — the refresh-token change

`SharedAuth/RefreshTokenEvaluator` no longer rejects a token presented from a different address.

`RefreshTokenTests` now covers rows 1–3 against a real database — the two tests that asserted the
old rejection were inverted rather than deleted, and rotation-after-an-address-change, the
`RefreshAddressChanged` audit row, its absence on an unchanged address, and reuse detection from the
issuing address are all pinned there. So this section is confirmation on a real device, not
discovery.

| # | Check | Expected |
|---|---|---|
| 1 | Sign in on wifi, switch to cellular, wait past the 15-min access token, use the app | **Session survives.** This used to be a silent sign-out |
| 2 | Audit log after that | A `RefreshAddressChanged` row |
| 3 | Capture a refresh token, let the real client rotate once, replay the old one | Whole token family revoked, `RefreshTokenReuseDetected` audited. **Theft detection must still work** |

---

## 4. Known gaps — not bugs, decisions

- **A guest's pending tickets cannot be opened.** No server id, and both detail screens fetch by one.
  If a guest showing a scannable code matters, the detail screens need to render from a queued
  request too. Currently out of scope.
- **PDF, calendar and wallet-pass import still need an account.** PdfPig and Ical.Net stayed
  server-side — AOT/trimming risk on iOS — and `PkPassTicketExtractor` throws GetThereAPI's
  `AppException`, which is its own untangling.
- **The map needs network for tiles.** MapLibre is vendored, so the page and its chrome load
  offline, but `tiles.openfreemap.org` still serves the basemap. Real offline mapping is a separate
  feature; `docs/architecture/map-features.md` already tracks it.
- **`RefreshToken.DeviceInfo` is not a device binding.** It is the raw `User-Agent`. The control that
  would replace IP pinning properly is a client-generated device id; recorded, not built.

## 5. If you only have time for five things

1. `dotnet build GetThere.slnx` and `dotnet test`.
2. `dotnet ef migrations has-pending-model-changes` in `GetThereAPI`.
3. Scan a rendered barcode with a real phone.
4. Load the map on an **Android** emulator.
5. Guest import → sign in → confirm the ticket lands on the server exactly once.
