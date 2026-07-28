# GetThereAPI — The Ticket Import Pipeline

## The problem this solves

A user already holds tickets that GetThere did not sell them: a PDF from a rail operator, an Apple
Wallet pass from an airline, a photo of a paper ticket, a calendar invite from a booking
confirmation. Without import, the app is a wallet that only holds what it sold — which is not a
wallet anyone would use as their travel wallet.

The pipeline exists to get those tickets in with as little typing as possible, **without ever
inventing data**. That second constraint shapes everything.

## Why extraction runs on the server

It would be plausible to decode a barcode on the phone. The decision to do it server-side was
deliberate, and the rationale is recorded in `Directory.Packages.props`:

- **Testable.** The extractors are ordinary classes exercised by `GetThere.Tests` with no device or
  emulator.
- **Identical on every platform.** iOS, Android and Windows would otherwise each have their own
  decoder with its own quirks — and HEIC support in particular varies by platform.
- **Fixable without an app release.** A scraper that mis-reads one operator's PDF layout can be
  corrected server-side and every client benefits immediately.

The cost is bandwidth and server CPU: a 10 MB upload per import, plus image decoding, barcode scanning
and PDF parsing. That cost is why the `Upload` rate limiter exists (10/min, against a global 100/min).

---

## The flow

```
       ┌─────────────────────────────────────────────────────────────────┐
       │  POST /importedtickets/upload   (multipart, ≤10 MB)             │
       └─────────────────────────────────────────────────────────────────┘
                                    │
              1. Bounded read       │  stop at 10 MB regardless of declared length
                                    ▼
              2. Sniff bytes        │  TicketFileSniffer.Detect  →  reject if unknown
                                    ▼
              3. Scan               │  ITicketFileScanner  (currently a no-op)
                                    ▼
              4. Select extractor   │  TicketExtractorRegistry.For(sniffedType)
                                    ▼
              5. Extract            │  BEFORE storing — an unreadable file never becomes a blob
                                    ▼
              6. Store + record     │  ITicketFileStore.SaveAsync → TicketUpload row (unconsumed)
                                    ▼
       ┌─────────────────────────────────────────────────────────────────┐
       │  { blobKey, fileType, contentType, sizeBytes, extraction }      │
       └─────────────────────────────────────────────────────────────────┘
                                    │
                   ── user reviews and corrects the fields in the app ──
                                    │
                                    ▼
       ┌─────────────────────────────────────────────────────────────────┐
       │  POST /importedtickets  { source, sourceFileBlobKey, …fields }  │
       │     → blob key resolved against caller's unconsumed uploads     │
       │     → dedupe check                                              │
       │     → ticket created, upload marked consumed (same SaveChanges) │
       └─────────────────────────────────────────────────────────────────┘
```

### Why nothing is created on upload

What a file yields varies enormously. A wallet pass is structured data and gives near-complete fields.
A photograph of a paper ticket with no barcode gives **nothing**. If upload created a ticket directly,
the second case would put an empty or half-guessed ticket in someone's wallet, and the first case
would still be wrong whenever an operator's field naming differed from what the extractor expected.

So extraction produces a *draft*. `TicketExtractionResult.DetectedFields` names which values were read
directly from the file, letting the UI distinguish "this is what your ticket says" from "this is our
best guess". `Warning` is set when the file was readable but yielded nothing worth prefilling — the
app shows it and falls back to a blank form rather than pretending success.

---

## Step 1 — the bounded read

Two independent limits:

- `[RequestSizeLimit(TicketUploadManager.MaxFileBytes)]` on the action — the framework's limit.
- `ReadBoundedAsync` inside the manager, which copies at most 10 MB and throws if anything remains.

The second is not redundant. `declaredLength` comes from a header the caller controls, and the copy is
what actually spends memory. A stream that under-reports its length cannot get more than one byte past
the ceiling.

---

## Step 2 — sniffing, and why the declared type is ignored

`TicketFileSniffer.Detect` reads the first 32 bytes and matches magic numbers. The multipart
`Content-Type` header and the filename are both chosen by the caller, so **neither can gate what the
extraction pipeline opens**. The sniffer is the allow-list: a format matching no signature is rejected
outright, and the *detected* type — never the declared one — selects the extractor.

| Type | Signature |
|---|---|
| JPEG | `FF D8 FF` |
| PNG | `89 50 4E 47 0D 0A 1A 0A` |
| PDF | `%PDF-` |
| PkPass | `50 4B 03 04` (ZIP local header) |
| WebP | `RIFF` at 0 **and** `WEBP` at 8 |
| HEIC | `ftyp` at 4, brand at 8 in `heic/heix/hevc/hevx/mif1/msf1/heim/heis` |
| iCalendar | `BEGIN:VCALENDAR` after optional BOM and whitespace |

Three of these are more subtle than they look:

- **RIFF** containers carry the real format at bytes 8–11; WAVE and AVI share the prefix, so matching
  `RIFF` alone would accept audio files.
- **ISO base media** (`ftyp`) covers MP4 as well as HEIC, so the brand must be checked or every video
  would pass as an image.
- **ZIP magic only proves "some archive".** It narrows the file to *might be a pkpass* — the extractor
  still has to find a readable `pass.json`, which is what separates a real pass from any other ZIP.
- **iCalendar has no binary signature at all**, being plain text, so it is identified by its mandatory
  opening line (RFC 5545 §3.4) after skipping a BOM and leading whitespace.

The sniffer also owns `ContentTypeFor` and `ExtensionFor`. The stored file's extension is derived from
the detected type, so **the uploaded filename never reaches the filesystem**.

---

## Step 3 — the scanner hook

`ITicketFileScanner` currently has one implementation, `NoOpTicketFileScanner`, which accepts
everything. It exists so that enforcing real malware scanning is a DI registration rather than a change
to the upload path:

```csharp
builder.Services.AddSingleton<ITicketFileScanner, NoOpTicketFileScanner>();
```

**This is a known open item, not a finished control.** Uploaded files are stored and served back to
their owner unscanned.

---

## Step 4 — extractor selection

`TicketExtractorRegistry` is built from every registered `ITicketExtractor`, indexed by each one's
`SupportedTypes`. Adding a format is a DI line plus a class:

```csharp
builder.Services.AddSingleton<ITicketExtractor, PkPassTicketExtractor>();
builder.Services.AddSingleton<ITicketExtractor, PdfTicketExtractor>();
builder.Services.AddSingleton<ITicketExtractor, ImageTicketExtractor>();
builder.Services.AddSingleton<ITicketExtractor, ICalTicketExtractor>();
builder.Services.AddSingleton<TicketExtractorRegistry>();
```

Registration order matters if two extractors claim the same type — the last one registered wins, since
the dictionary is built by assignment.

Each extractor also declares `SourceFor(fileType)`, which is the `ImportSource` recorded on tickets
created from that file type. That is what gives a ticket its provenance in the wallet.

| Extractor | Handles | Source |
|---|---|---|
| `PkPassTicketExtractor` | PkPass | `PkPass` |
| `PdfTicketExtractor` | Pdf | `Pdf` |
| `ImageTicketExtractor` | Jpeg, Png, Webp, Heic | `Photo` |
| `ICalTicketExtractor` | ICalendar | `Calendar` |

---

## The extractors

### `PkPassTicketExtractor` — the best source available

An Apple Wallet pass is a ZIP whose `pass.json` carries the ticket as structured data: organiser,
dates, origin and destination, and the barcode payload **already decoded**. Nothing has to be guessed
from prose or recovered from a photograph. It is also the format airlines and rail operators actually
email out, which is why it gets the most attention.

Origin and destination are found by matching field keys against known variants, because operators do
not agree on naming:

```
origin:      origin, from, depart, departure, boarding, source
destination: destination, to, arrive, arrival, dest
```

**Zip-bomb defences.** The archive is attacker-controlled, so without bounds a few KB of upload can
expand into gigabytes:

| Bound | Value | Guards against |
|---|---|---|
| `MaxEntries` | 64 | Archives with millions of tiny entries |
| `MaxEntrySize` | 2 MB | One entry expanding hugely |
| `MaxTotalSize` | 16 MB | Many entries summing hugely |
| `MaxCompressionRatio` | 200:1 | Highly compressible padding |

A pass is a handful of small JSON and PNG files, so these are far above anything legitimate.
TransitInfoAPI guards GTFS zips the same way — the shared reasoning is that any archive from outside
is hostile until bounded.

A ZIP with no `pass.json` is rejected with 400 rather than treated as empty, because the sniffer only
proved it was an archive.

### `PdfTicketExtractor` — two passes over one file

A PDF e-ticket is read two ways:

1. **The text layer**, scraped by `TicketTextScraper` for route, dates and price.
2. **Embedded images**, scanned by `BarcodeDecoder` for the barcode operators print on the ticket.

The barcode is the more valuable of the two: it is the machine-readable ticket itself, and it survives
the layout differences between operators that defeat text scraping.

Bounds: `MaxPages = 20` (an e-ticket is a page or two; beyond that it is a document) and
`MaxImagesScanned = 12` (scanning every image on every page is unbounded work an upload should not
buy). A PDF that cannot be opened is a 400, not a 500 — an unreadable upload is the user's problem to
correct, not a server fault.

### `ImageTicketExtractor` — codes, not prose

This path finds **codes**, not text. A QR or Aztec symbol decodes to the ticket payload; a photograph
of a plain paper ticket carrying no code yields nothing, and **says so** rather than inventing fields.
That is the honest boundary until an OCR engine is added.

When a payload does decode, it is additionally run through `TicketTextScraper`, because some operators
encode readable text into the payload itself. UIC 918-3 rail payloads are signed binary and yield
nothing there — which is fine, the payload is still stored and remains the useful part.

HEIC gets its own warning message, because SkiaSharp's ability to decode it varies by native build.
The MAUI client re-encodes camera captures to JPEG for exactly this reason; a HEIC picked from disk
that lands here simply yields no barcode rather than failing the upload.

### `ICalTicketExtractor` — the only format that states the window

Operators routinely attach a calendar invite to a booking confirmation, and it is the only common
format that states the journey window **explicitly** rather than leaving it to be inferred from prose.

When a file carries several events (a confirmation may include the outbound leg plus reminders), the
**earliest** is taken — the journey itself is the one that starts first. `Summary` becomes the ticket
name, `Location` becomes the route description.

---

## `BarcodeDecoder` — why not just QR

The format list matters more than it looks:

```
QR_CODE, AZTEC, PDF_417, DATA_MATRIX, CODE_128, CODE_39, EAN_13, ITF
```

**Aztec and PDF417 are as important as QR here.** European rail tickets following UIC 918-3 use them,
so a decoder limited to QR would miss most train tickets — the single most common thing this pipeline
is pointed at.

SkiaSharp decodes the image into the pixel buffer ZXing needs. A `null` result means "no code found",
which callers treat as *nothing to prefill* rather than as a failure. That distinction is the whole
contract of this class.

---

## `TicketTextScraper` — deliberately conservative

Operators have no common text layout, so everything this produces is a suggestion the user reviews.
The stated principle: **it would rather leave a field blank than fill it with something plausible and
wrong.** Only fields it is reasonably sure of are added to `DetectedFields`.

It uses source-generated regexes (compile-time, no runtime regex construction):

| Pattern | Matches |
|---|---|
| `DayFirstDate` | `31/12/2026`, `31.12.2026`, `31-12-2026` |
| `IsoDate` | `2026-12-31` |
| `TimeOfDay` | `23:59` (hour bounded 0–23, minute 0–59) |
| `Money` | `12,50 EUR` **or** `EUR 12.50` / `€12.50` |
| `RouteLine` | `Zagreb → Rijeka`, `Zagreb - Rijeka`, `Zagreb to Rijeka` |
| `BookingReference` | `booking`/`reservation`/`reference`/`ref`/`pnr`/`order` followed by 5–12 alphanumerics |

`Money` handles both orderings because both conventions appear in practice, and maps `€`/`$`/`£` to
`EUR`/`USD`/`GBP`. `RouteLine` is anchored to a whole line — a mid-sentence "to" is not a route
separator.

This is also what backs `POST /importedtickets/extract-text` for pasted confirmation emails. That path
has no file, so it needs no storage and mints no blob key — which is why `ImportSource.Text` joins
`Manual` as a source that does not require one.

---

## Storage

### `ITicketFileStore` and the local implementation

The interface exists so that swapping local disk for object storage later means replacing one class.
The current `LocalTicketFileStore` writes under `{ContentRoot}/ticket-files/{userId}/{blobKey}`, or
under `TicketFiles:RootPath` when configured. The stated reason for disk: this deployment has no cloud
storage of any kind — no Azure, no S3, no credentials — and TransitInfoAPI already keeps GTFS feeds on
disk the same way.

**Keys are server-minted.** `SaveAsync` generates `{Guid:N}{extension}`, where the extension comes from
the sniffed type. No caller input reaches the path at all.

**Writes are atomic.** Content goes to `{path}.tmp` and is then moved into place, so a failed or
cancelled write cannot leave a truncated file under a key the database already believes in.

### Path containment, and why it is belt-and-braces

`ResolvePath` applies two independent guards even though keys are server-minted:

1. `EnsurePlainFileName` rejects `/`, `\`, `..`, `\0`, and rooted paths — **as a string, before the
   value is ever treated as a path**. This is the important ordering: what counts as a separator is
   platform-dependent, so `"..\..\etc\passwd"` is an inert filename on Linux but escapes on Windows.
   Checking the string means the guard does not change meaning with the deployment target.
2. The resolved absolute path must start with the user's own directory plus a separator.

The comment explains why a "cannot happen" case is still checked: a previous path-traversal defect in
`FeedManager.GetFeedStorageDirectory` came from trusting an identifier that "could not" contain
separators.

### The `TicketUpload` row — what makes client-named files safe

This is the piece that makes it acceptable to let a client name a stored file at all.

The blob key is minted server-side and recorded against its owner. `POST /importedtickets` then
resolves it with:

```csharp
u.BlobKey == blobKey && u.UserId == userId && u.ConsumedAt == null
```

so a key belonging to someone else, a key already spent, and a made-up string are **all
indistinguishable from not existing**. Without this row, `SourceFileBlobKey` would be an arbitrary
caller-supplied storage path.

`ConsumedAt` is set in the **same `SaveChanges`** as the ticket insert. If the insert is rejected —
by the dedupe index, say — the key is not burned, and the user can retry with the file they just
uploaded.

### Abandoned uploads

A user who uploads and then abandons the form leaves a blob nothing references.
`PurgeAbandonedAsync`, called each pass of `TicketExpiryWorker`, deletes uploads with
`ConsumedAt == null` older than **24 hours** (`UnconsumedRetention`).

A blob that fails to delete is logged and its row removed anyway — otherwise the sweep retries the
same file forever and never reaches the rest of the backlog.

---

## Known gaps

| Gap | Consequence |
|---|---|
| `NoOpTicketFileScanner` | Uploaded files are stored and served back unscanned |
| No OCR | A photo of a ticket with no barcode yields nothing |
| Files are not encrypted at rest | Disk access exposes ticket files |
| `Verification` is always `Unverified` | Nothing sets `Verified`/`Suspicious`; the field is reserved for a check against the issuing operator that does not exist yet |
