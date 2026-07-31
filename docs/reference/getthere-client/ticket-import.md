# GetThere (MAUI Client) — Ticket Import, View Models and UI Patterns

## Why there is no barcode scanner in the app

The most consequential decision on the client side of import is what is **absent**:

> Uses the built-in `MediaPicker` and `FilePicker` rather than adding a scanner package: the server
> already decodes QR, Aztec and PDF417 out of an uploaded image, so photographing a code and scanning
> one are the same operation from the client's side.

This removes a native dependency from four platforms, and it means decoder improvements ship
server-side without an app release. The cost is that "scanning" requires a round trip — there is no
live camera preview that locks onto a code. For importing a ticket, which happens once per ticket
rather than continuously, that is the right trade.

---

## The import flow

Every source funnels into one form:

```
   Camera        Photo library      File picker        Paste text        Type by hand
      │                │                 │                  │                 │
      └────────┬───────┴─────────────────┘                  │                 │
               ▼                                            ▼                 │
     TicketCaptureService                        POST /extract-text           │
     (re-encode, orient, downscale)                         │                 │
               ▼                                            │                 │
     POST /importedtickets/upload                           │                 │
               ▼                                            ▼                 ▼
          TicketImportDraft.FromUpload            FromText              (empty draft)
               └──────────────────────┬─────────────────────┴─────────────────┘
                                      ▼
                          ImportTicketPage / ViewModel
                        the single confirmation surface
                                      ▼
                          POST /importedtickets
```

The stated reason for the funnel:

> Every branch ends on the same form: it is the one place a ticket is confirmed and validated, so a
> scanned pass and a typed ticket take the same path to being saved.

One validation path rather than five, and nothing reaches the server unreviewed.

---

## `TicketCaptureService`

Picks a file and **normalises images** before upload.

| Constant | Value | Reason |
|---|---|---|
| `MaxFileBytes` | 10 MB | Mirrors `TicketUploadManager.MaxFileBytes`. Checked here only so an oversized pick fails immediately instead of after uploading 10 MB to be rejected — **the server enforces it** |
| `MaxImageEdge` | 2400 px | *Generous on purpose*: downscaling too far destroys the fine structure of a QR or PDF417 symbol, which is the whole point of the upload |
| `JpegQualitySteps` | 85, 70, 55 | Tried in order until the result fits under the ceiling |

`CapturedTicketFile` carries content, filename and content type — and the record's own doc comments
label the last two **advisory only**: the server sniffs the bytes and never trusts either.

### Why re-encoding exists at all: HEIC

This is the single most valuable thing the capture service does.

> iOS hands back HEIC by default, and server-side HEIC decoding is the most fragile path SkiaSharp
> has — `BarcodeDecoder` treats an undecodable image as "no barcode found" rather than an error, so an
> un-normalised HEIC silently prefills nothing. Converting on the device, where the platform's own
> HEIC decoder is available, turns that into an ordinary JPEG the server can always read.

The failure it prevents is the *quiet* kind: not an error, just an import that mysteriously extracts
nothing. Doing the conversion where a guaranteed-present platform decoder exists is what makes iOS
imports work at all.

If re-encoding fails, the original bytes are uploaded untouched and the server gets its own attempt —
strictly better than failing the import.

### EXIF orientation, applied by rewriting pixels

`SKBitmap.Decode` ignores EXIF, so a portrait phone photo decodes sideways. `ApplyOrigin` bakes the
orientation into the pixels via an affine transform, handling all eight EXIF origins.

The reasoning is not about decoding:

> ZXing's `AutoRotate` would still find the code, but the stored file is served back to the user later
> and would be displayed rotated, so bake the orientation in here.

The barcode would decode either way. It is the *stored file* — retrievable at
`GET /importedtickets/{id}/file` — that must look right. And since JPEG re-encoding here does not
carry EXIF over, rewriting pixels is the only option.

### Memory discipline

Both `ApplyOrigin` and `Downscale` **return their input unchanged when there is nothing to do**, and
the disposal guards use `ReferenceEquals` so an unmodified bitmap is not disposed twice:

```csharp
finally { if (!ReferenceEquals(upright, decoded)) upright.Dispose(); }
```

> A 12 MP capture is ~48 MB decoded, so the copies this avoids are the difference between comfortable
> and an out-of-memory kill on a low-end device.

`Downscale` uses **Mitchell cubic resampling**, not the default linear filter:

> downscaling a photographed QR or PDF417 softens the module edges the decoder keys off, and Mitchell
> preserves them with little ringing.

`ApplyOrigin` conversely uses default sampling — the matrix only ever rotates or mirrors by whole
quarter-turns, so no pixel is resampled between grid positions.

### The extension check is a hint, not a gate

```csharp
// The extension is a hint about whether re-encoding is worth attempting, never a security
// decision — the server sniffs the bytes regardless. Skipping it for a PDF just avoids
// handing 10 MB to an image decoder that will refuse it.
```

Worth internalising, because it is the same principle as the server's sniffing: filenames and
declared content types are hints for efficiency, never for safety.

`PickPhotosAsync` (plural) is used because the singular form is obsolete, but **only the first photo
is taken** — one ticket per import keeps the confirmation form a single reviewable draft.

---

## `TicketImportDraft`

The transport object between capture and form. Nothing exists on the server yet: a file has been
uploaded and read, but no ticket is created until the user confirms.

| Property | Meaning |
|---|---|
| `Source` | `ImportSource` recorded on the ticket |
| `BlobKey` | Single-use handle to the stored file; null for pasted text and manual entry |
| `Extraction` | What the server read — every field a candidate |

`QueryKey = "draft"` is passed as an **object** through `IQueryAttributable`, never serialised into a
query string.

### Deriving the source client-side

```csharp
private static ImportSource SourceFor(TicketFileType fileType, TicketExtractionResult extraction) => fileType switch
{
    TicketFileType.Pdf       => ImportSource.Pdf,
    TicketFileType.PkPass    => ImportSource.PkPass,
    TicketFileType.ICalendar => ImportSource.Calendar,
    _ => extraction.PayloadFormat is not null ? ImportSource.QrScan : ImportSource.Photo
};
```

The remark explains why the client decides this at all:

> The source is decided here rather than taken from the response because `TicketUploadResponse`
> carries no source — the server's `ITicketExtractor.SourceFor` exists but is never surfaced by the
> endpoint. `FileType` is the sniffed truth, so deriving from it still means the bytes decide, not
> the filename.

**This is a genuine duplication of server logic**, flagged honestly. The mapping exists in both
`ITicketExtractor.SourceFor` and here, and they can drift. The clean fix is to add `Source` to
`TicketUploadResponse`.

The image branch is the interesting one: an image that yielded a symbol is a **scan**, one that did not
is a **photograph**. That distinction is what the wallet shows as provenance, and it feeds the
server's dedupe hash — deterministic either way, since it derives from the file's own content rather
than from how the user happened to pick it.

---

## `ImportTicketViewModel`

The single confirmation surface. It receives a draft via `IQueryAttributable`, prefills every field,
and lets the user correct anything before saving.

Bindable state: `TicketName`, `RouteDescription`, `OriginName`, `DestinationName`, `PriceText`,
`SelectedCurrency`, `ValidFrom`, `ValidTo`, `OperatorName`, plus `ErrorText`/`HasError` and
`DraftSummary`/`HasDraft`.

`DraftSummary` is the *headline telling the user what was read off their file, or that nothing was* —
built from `Extraction.DetectedFields` and `Extraction.Warning`. This is where the server's careful
distinction between "read from the file" and "our best guess" reaches the user; without it, a prefilled
field looks equally authoritative whether it was extracted or invented.

`PriceText` is a **string**, not a decimal, because a partially-typed value like `"12."` is not
parseable and binding to a decimal would fight the user mid-keystroke. Parsing happens on save.

`ValidFrom`/`ValidTo` default to today and tomorrow — a sensible window for a ticket being imported now.

### Duplicate handling

```csharp
public const string DuplicateCode = "DUPLICATE_TICKET";
```

The server returns 409 when a ticket matches an existing active one's dedupe hash. The client shows
the clash and offers to import anyway, resending with `AllowDuplicate = true`. This is the
client half of the design decision recorded in
[../getthere-api/endpoints.md](../getthere-api/endpoints.md#the-two-step-create-and-why-it-is-two-steps):
two passengers on the same route on the same day are a legitimate pair of tickets, and a hard
rejection left them no way through.

---

## Other view models

### `TicketsViewModel`

The wallet list. Paged (`_currentPage`, `HasMore`, `TotalTickets`) with filter chips over
`ImportedTicketStatus`.

`ActiveFilterKey` is an **empty string for "All"**, otherwise the status name — a string rather than a
nullable enum because it binds directly to chip styling converters.

`UpdateStatusAsync` on `ImportedTicketService` carries a pointed comment:

> Without this the API's PATCH endpoint was unreachable, so nothing could ever mark a ticket used —
> while the list still rendered a "Used" filter chip that was permanently empty.

A good example of a whole-stack gap: the endpoint existed, the UI existed, the client method did not.

### `TicketPurchaseViewModel`

Reached as `ticketpurchase?adapterId=N` via `[QueryProperty]`. Loads the operator's fares and the
wallet balance together, since the commit needs both.

`PurchaseOption` wraps a `TicketOptionResponse` with an `IsSelected` flag — *selection is a view
concern, so it lives here rather than on the contract type*. That principle keeps `GetThereShared`
free of UI state.

`HasNoOptions` is deliberately **separate from `!HasOptions`**, true only once a load has finished and
found nothing — so the empty state does not flash during loading.

`TicketService.PurchaseTicketAsync` always sends an `Idempotency-Key` (`Guid.ToString("N")`, 32 hex
characters, inside the server's 8–64 range). The doc comment states the stakes plainly: the API charges
the wallet, and `AuthenticatedHttpHandler` replays the request after a 401 refresh — without a key that
replay is a second purchase.

The API distinguishes *retrying the same user action* (same key) from *a new purchase* (fresh key).

### `MapViewModel`

Owns the map URL and the transport-mode chips, and raises `ModeFilterChanged` rather than touching the
WebView. See [architecture.md](architecture.md#the-map-a-webview-and-how-the-token-reaches-it).

---

## UI patterns worth knowing

### `PageUtility` and the value converters

`PageUtility` holds shared page helpers — `ShowError`/`HideError`, `SetBusy`, email and phone
validation, and `ApplyResponsiveWidth`, which constrains content to a ratio of page width with a
minimum, so one XAML layout serves phone and desktop.

The converters in the same file encode design decisions:

| Converter | Purpose |
|---|---|
| `TicketPriceConverter` | **Multi-binding** on price *and* currency |
| `TicketStatusColorConverter` | Status → badge colour; `"surface"` parameter for background |
| Opacity converter | Used/Expired dimmed rather than hidden |
| `FilterChipConverter` | Single-selection chips; parameter `"Active:bg"` |
| Bool chip converter | Self-owning chips (map modes); parameter `"bg"`/`"stroke"`/`"text"` |
| Has-content converter | Optional lines, e.g. the adapter slug |

`TicketPriceConverter` fixes a specific bug and explains it:

> The ticket list previously formatted every price with a hardcoded "EUR" suffix while each ticket
> carries its own currency and the import form offers a picker, so a ticket saved in GBP was displayed
> to the user as euros.

It is a **multi-binding** because neither value alone can render the price, and it defers to
`MoneyFormatter` so wallet balances and ticket prices format identically. That is why `MoneyFormatter`
lives in `GetThereShared` rather than in the client.

Dimming rather than hiding spent tickets is a deliberate design choice: *the history stays visible
without competing with live tickets.*

### Styling

Three resource dictionaries: `Colors.xaml` (palette), `DesignSystem.xaml` (spacing, typography,
component styles), `Styles.xaml` (control styles). Theme-aware assets use `SetAppTheme` /
`AppThemeBinding`.

### `BaseViewModel`

Minimal by design:

```csharp
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isAuthenticated;
}
```

Its real job is being the **marker type the DI registration scan matches on**. A view model that does
not derive from it is never registered.

### `AnalyticsService`

`IAnalyticsService` has `TrackEvent` and `TrackScreen`. The implementation is currently a **stub** —
the abstraction exists so wiring a real provider is one registration change. `AppShell` already calls
`TrackScreen` on every navigation, so the call sites are in place.

---

## Known gaps

| Gap | Impact |
|---|---|
| MapLibre is loaded from a CDN | If unpkg is unreachable the map page renders nothing at all — chrome included, now that the chrome lives in the page. Vendoring it into `wwwroot` removes the dependency |
| `ApiMessageMapper` covers auth codes only | Money-path errors show in English |
| `AnalyticsService` is a stub | Events go nowhere |
| `SourceFor` duplicates server logic | Can drift; fix is to add `Source` to `TicketUploadResponse` |
| Suggestion dismissal is not persisted | A dismissed proposal returns on the next load |
| No "add existing ticket to a journey" flow | Tickets join a journey via a suggestion or at creation; there is no picker on an existing journey |

---

## Journeys

Journeys live behind a **segmented control on the Tickets screen** — `Tickets | Journeys` — rather
than a fifth tab, because `AppShell` documents that the phone frames have no room for one.

```
TicketsViewModel
  ├── ImportedTickets          the Tickets half
  └── Journeys : JourneysViewModel    the Journeys half
```

`JourneysViewModel` is **composed, not merged**. Journeys and tickets are two views of one wallet, but
their loads, busy flags and error states are independent — a failing journeys call must not blank the
ticket list. It is registered by the same namespace convention as every other view model and injected
into `TicketsViewModel`.

Journeys load **the first time the segment is opened**, gated on `HasLoadedOnce`, rather than on every
page appearance: most sessions never open it. The flag is set even when the load fails, so a failure
shows its error instead of silently retrying on each switch.

### Suggestions

`GET /journeys/suggestions` proposals render as outlined cards above the list, each showing the
server's `Reason` verbatim — it is written to be shown directly. Accepting one is a **single create**
carrying the suggested ticket ids, not a create plus N adds.

`LoadSuggestions` runs after the list and outside its `try`, and swallows its own failures. There is
no user-visible feature to degrade: the card simply does not appear. Dismissing is session-only and
nothing is persisted.

### `JourneyDetailPage`

Reached as `journeydetail?journeyId=N`. Shows the trip, its legs ordered in time, and rename / cancel /
delete.

Two details carry real risk:

**`JourneyLegItem` always carries `IsImported`.** A leg's `Id` alone does not identify it — imported
and purchased tickets share an id space only by accident — so removal puts the id in
`ImportedTicketIds` or `TicketIds` accordingly. Getting that wrong removes a different ticket.

**The total is hidden when legs disagree on currency.** There is no conversion anywhere in the system,
so summing across currencies would produce a number that is simply wrong — the same reasoning that
makes the API reject a cross-currency purchase.

Only `Cancelled` is offered as a status. `Planned`/`Active`/`Completed` are recomputed server-side from
the legs by the expiry sweep, so offering them would produce a value that silently reverts.

Delete and remove-leg are both phrased around what actually happens: the grouping goes, the tickets
stay in the wallet.

### `Show journey` on a ticket

`TicketDetailPage`'s button is bound to `ShowJourneyCommand` and visible only when the ticket is in a
journey. This required adding **`JourneyId` to `TicketResponse`** (and to `TicketMapper`), mirroring
`ImportedTicketResponse` — a purchased ticket is a journey leg just as an imported one is, and the
contract had no way to say so.

The adjacent `Add to Wallet` button was **removed**: it had no `Command` binding at all, and
Apple/Google Wallet export is not built. Its `Ticket_AddToWallet` string was deleted from both resource
files.
