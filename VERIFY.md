# Verification checklist — `claude/maui-ui-to-transitinfo-map-9cj09m`

**Disposable.** This exists to get one branch verified. Delete it once the branch is merged — it is
not part of the permanent docs.

## Read this first

**Nothing on this branch has been compiled or run.** It was written in a container with no .NET SDK,
and the installer is blocked by egress policy. Every claim below about behaviour is derived from
reading the code, not from observing it. Treat the build as the first real gate and everything after
it as unproven.

Static checks that *were* done, so you can skip re-deriving them: no references remain to any deleted
or moved symbol; `.resx` parity holds at 281 keys in both cultures; every `.csproj`, `.xaml`,
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

**Most likely to be red, in order:**

1. **`GetThereExtraction` is a new project.** It was added to `GetThere.slnx`, both apps, the test
   project, and all four places `build-check.yml` names projects individually. If CI passes but a
   local `dotnet build GetThere.slnx` fails, suspect the solution entry.
2. **`BarcodeRenderService`** uses ZXing APIs (`BarcodeWriterPixelData`, `QrCodeEncodingOptions`,
   `ErrorCorrectionLevel`) that were written from memory against ZXing.Net 0.16.11.
3. **`TicketStore`** uses `AesGcm` and `SKBitmap.InstallPixels` over a pinned buffer.
4. **XAML bindings** — `TicketsPage` and the two detail pages changed shape. The ticket card's
   `DataTemplate` is untyped, so a wrong property name fails at runtime, not compile time.

## 2. The database change — check this before running anything against a real database

`GetThereAPI/Migrations/20260731120000_AddImportedTicketClientId.cs` **and** the matching
`AppDbContextModelSnapshot.cs` edit were written **by hand**, because there was no `dotnet ef`. This
was an explicitly granted exception to `AGENTS.md`'s "never manually edit `*ModelSnapshot.cs`".

```bash
# Should report no pending model changes. If it wants to scaffold another migration,
# the hand-written snapshot does not match the model — trust the tool, not the file.
cd GetThereAPI && dotnet ef migrations has-pending-model-changes
```

The DDL is two statements and is low risk. The snapshot agreeing with the model is the part worth
verifying. If it disagrees, delete both hand-written files and re-scaffold:
`dotnet ef migrations add AddImportedTicketClientId`.

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
| 10 | GetThereAPI admin console | Still shows "TransitInfoAPI reachable" — the one surviving proxy endpoint |
| 11 | `/api/map/upstream/stations` | 404. The passthrough is gone |

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
