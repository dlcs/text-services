# TextServices — Design Decisions & Interaction Log

This file records key design decisions, questions, and responses made during development.
Code-level decisions are in CLAUDE.md. This log covers the *why*.

---

## 2026-03-29 — Initial design session

### Context
Tom Crane (Digirati) briefed Claude on the requirements via `instructions/text-services.md`.
Claude read both reference implementations in full:
- Wellcome Collection: `C:/git/wellcomecollection/iiif-builder/src/Wellcome.Dds/`
- St Louis Fed: `C:/git/digirati-co-uk/st-louis-fed/src/IIIFBuilder/`

### Questions asked and answers given

**Q1: Root namespace / solution name?**
> "TextServices"

**Q2: Job queue — simple in-process Channel+BackgroundService vs Hangfire vs other?**
> "I am not familiar with Hangfire, but am willing to try it out. I assume that it will work fine in AWS, Azure etc as well as local filesystem?"

*Confirmed: Hangfire uses a pluggable storage backend (PostgreSQL, Redis, SQL Server). Works identically with local PostgreSQL, AWS RDS, and Azure Database for PostgreSQL. Just change the connection string.*

**Q3: AutoComplete storage — embedded in Text object (Wellcome pattern) vs separate file (St Louis pattern)?**
> "Let's go for the St Louis separate file pattern - unless you recommend otherwise"

*No counter-recommendation — separate storage is the better design: the Search API can load AutoComplete independently from the (larger) Text object when serving autocomplete-only requests.*

**Q4: IIIF JSON building — use iiif-net NuGet package or plain JSON?**
> "I think keep to simple JSON for now. We might use iiif-net later but I don't think it's necessary right now."

**Q5: MediatR — use it or not?**
> "Use MediatR"

### Additional request
Tom asked for this interaction log to be kept in the repo:
> "is it possible for you to log all of our interactions on this project (not the code churning, just the main interactions and my responses to them) so we have a record"

*This file is the result. It will be updated at the end of each significant working session.*

### Agreed architecture summary
- Three logical components: Core class library, Builder API, Search API
- Five C# projects + two test projects (see CLAUDE.md for full structure)
- protobuf-net serialisation (proven in production in both reference implementations)
- Hangfire + PostgreSQL for durable job queue
- EntityFramework Core + Migrations for job state
- MediatR in both APIs
- Plain `System.Text.Json` for IIIF JSON (no iiif-net for now)
- `ITextFormatProvider` interface for format extensibility (ALTO first, hOCR etc. later)
- ALTO ns-v2 and ns-v3 both supported
- IIIF Search v1 first; v2 phased in later
- Builder API and Search API are separate ASP.NET applications

---

## 2026-03-29 — PR 1 decisions

### FluentAssertions → Shouldly

**Q: FluentAssertions 8.x showed a commercial licence warning in test output. What alternative do you recommend?**
> "yes please and update the PR"

Switched to **Shouldly 4.x** (MIT licence). Near-identical expressiveness; `.Should().Be(x)` becomes `.ShouldBe(x)` etc. One minor difference: nullable `string?` properties need a null-assertion step before calling string-specific methods like `ShouldContain`.

### Thorough comparison with reference implementations (prompted by PR review)

Tom asked for a direct line-by-line comparison of the `Text` implementation against both references.
Several differences were found and corrected:

**Search algorithm**: original used LINQ filtering over all words; reference walks *back* from the
IndexOf hit to the word boundary, then steps forward via `word.LenNorm + 1`. Substring matches
(e.g. "ick") correctly expand to the whole containing word ("quick"). `startPos` advances past
all matched words, not just `+1`.

**AddContext**: original used 6-word context on first/last rect of each hit group; reference uses
150 raw characters before/after, applied to **every** ResultRect individually, and skipped entirely
when there are ≥100 results.

**Coalescing bounding box**: reference uses `Math.Min(Y)` / `Math.Max(H)` independently (not
geometrically perfect but consistent with production behaviour — matching for now).

**Coalescing adjacency check**: reference checks only `Li` + `Wd` (not `Idx`). Extra `Idx` check removed.

**ResultRect / Word methods**: added `ToString()`, `ToRawString()`, `ShallowCopy()` to match the
reference's method contracts used during coalescing.

**Hit numbering**: single-word queries use element index (0-based) as hit number, matching the
reference's use of the two-argument `Select` overload.

### Text normalisation — drop vs replace

**PR review question:** Is the normalisation correct re the reference implementations?

Both Wellcome and St Louis Fed use `ToAlphanumericOrWhitespace` which **drops** non-alphanumeric characters (keeping existing whitespace), rather than replacing them with spaces. Corrected in response to review:
- `"it's"` → `"its"` (not `"it s"`)
- `"foo-bar"` → `"foobar"` (not `"foo bar"`)
- `"hello, world"` → `"hello world"` (comma dropped; adjacent space preserved)

### .gitignore fix

The standard Visual Studio `.gitignore` template includes `*.e2e` (for VS Trace files). This pattern also matched the `src/TextServices.Tests.E2E/` project directory, silently excluding it from git. Fixed with a `!*Tests.E2E/` negation rule.

---

## 2026-03-29 — PR 1 additional tests and bug fixes

### Motivation

Tom asked for more comprehensive unit tests covering longer text, spaces, punctuation, and other problematic characters, to give confidence that normalisation maximises search hit chances.

### Two bugs found by the new tests

**Bug 1 — `startPos` over-advance in `Text.Search`**

`startPos = matchPos + matchLength + 1` was advancing one position too many. `matchLength` already includes the trailing space for each word (`LenNorm + 1`), so the extra `+1` caused the next search to start *past* the first character of the following word. Concretely, searching `"pre"` in `"pre prefix word"` returned 1 hit instead of 2 because `"prefix"` starts at position 4 but `startPos` advanced to 5. Fixed: `startPos = matchPos + matchLength`.

**Bug 2 — Cross-page coalescing in `GetRectangles`**

The adjacency check `current.Li == next.Li && current.Wds[^1] == next.Wd - 1` did not include `Idx` (image index). Because `_lineCounter` resets to 0 on `BeginPage`, the last line of page N and the first line of page N+1 both get `Li = 1`. Combined with consecutive global `Wd` values, two words on different canvases were incorrectly merged into a single `ResultRect` — which would produce wrong coordinates in a IIIF Search response. Fixed by adding `&& current.Idx == next.Idx`. (Earlier analysis of the reference implementations noted that they check only `Li + Wd`; that observation was correct for single-page documents but the per-page reset of `Li` in our `TextAccumulator` means `Idx` is also required.)

### New test files added

- `TextNormalisationEdgeCaseTests.cs` — 40+ theory/fact tests covering contractions, curly apostrophes, hyphens, en/em dashes, abbreviations, numbers with formatting, brackets, ellipsis, whitespace variants (including U+00A0 non-breaking space), accented character preservation (documented known limitation), and the symmetry property over realistic sentences.
- `TextSearchEdgeCaseTests.cs` — integration tests over multi-word passages: query/text punctuation symmetry, substring matching capturing whole words, multi-word phrases in long passages, repeated word counting, document boundary behaviour, two-page non-coalescing, punctuation-only tokens skipped, context at document edges, empty/degenerate inputs.

All 138 tests pass.

---

## 2026-03-29 — Second reference comparison pass (at Tom's request)

Tom asked for a further line-by-line comparison of all Core classes against both references before going further.

### Li is a global (not per-page) line counter — critical fix

Both references keep `lineCounter` as a single counter across all pages, never reset between pages. `Word.Li` is documented in both references as *"The unique line number within the document"*. Our `TextAccumulator.BeginPage` was incorrectly resetting `_lineCounter = 0`, making Li per-page.

Consequence: the last line of page N and first line of page N+1 both received Li=1. Combined with consecutive global `Wd` values, words on different pages could satisfy the coalescing check `current.Li == next.Li && current.Wds.Last() == next.Wd - 1`, causing them to be merged into a single `ResultRect` — which would produce wrong coordinates in a IIIF Search response.

Fix: removed the `_lineCounter = 0` reset from `BeginPage`. The Idx guard added as a workaround in the previous commit was then removed as redundant; the references don't have it and with globally-unique Li it isn't needed.

### `startPos + 1` is a bug in both references — our earlier fix retained

Both references have `startPos = matchPos + matchLength + 1`. This over-advances by one: `matchLength` already includes the trailing space separator, so `+1` skips the first character of the next word. Example: searching `"pre"` in `"pre prefix"` returns only 1 hit in the references instead of 2. Our removal of the `+1` is demonstrably more correct and is retained as an intentional improvement. Documented here rather than silently matching the references.

### Full comparison results — everything else matches

After both passes, the following are confirmed correct against both references:

- Protobuf field numbers (Word 1–12, Image 1–2, ComposedBlock 1–4)
- All Word/ResultRect/Image properties and method contracts
- Search algorithm: IndexOf → word-boundary walk-back → step-by-step word collection
- Single-word path: element index as hit number (equivalent to two-arg Select)
- Multi-word path: Li+Wd coalescing, Math.Min(Y)/Math.Max(H), ShallowCopy
- AddContext: per-rect, 150 raw chars, skipped when ≥100 results
- AutoComplete: 3-char prefix buckets, `length then alpha` ordering, >2 char threshold

Intentional extensions beyond the references:
- `ComposedBlock` has X, Y, W, H, BlockType (Wellcome has coordinates and type embedded differently; added here for completeness)
- AutoComplete stored as a separate file (St Louis pattern), not inside Text (Wellcome pattern)
- `StringComparison.Ordinal` in `IndexOf` rather than `InvariantCultureIgnoreCase` (equivalent for lowercase-only normalised text; Ordinal is marginally faster)
- Empty-norm words are skipped entirely by TextAccumulator rather than being written to raw but not norm text (St Louis partial-skip behaviour); our approach is cleaner

---

## 2026-03-29 — PRs 3–5 (Storage, Builder API scaffold, Manifest fetching)

### PR 3 — Storage abstraction

`ITextStore` interface and `FileSystemTextStore` introduced. Keys containing `/` are treated as path segments so `"2/books/my-book"` maps to `{RootPath}/2/books/my-book/`. Three fixed filenames per key: `text.bin`, `autocomplete.bin`, `manifest.json`. `Exists()` checks for `text.bin` only (AutoComplete or Manifest alone does not constitute a complete artefact).

### PR 4 — Builder API scaffold

PostgreSQL credentials go in `appsettings.Development.json`, which is already gitignored (the `.gitignore` rule `appsettings.Development.json` was already present). `appsettings.json` carries an empty `ConnectionStrings:BuilderDb` placeholder. `JobStatus` enum uses `Waiting` (not `Pending`) for the initial state.

### PR 5 — IIIF Manifest fetching

Neither reference implementation handles IIIF Manifests (both use METS). This is a new capability, so the design follows CLAUDE.md directly with no reference precedent.

`ManifestReducer` treats the manifest as plain JSON (`System.Text.Json`); no IIIF object model needed. v3 detection checks `@context` for `"presentation/3"` (string or array); falls back to `type = "Manifest" + items` when context is absent. v2 manifests throw with a clear message. ALTO detection checks `profile` or `label` for the string "alto" (case-insensitive); `label` may be a IIIF lang map (`{"none": ["METS-ALTO"]}`) or a plain string.

### Canvas dimension handling — time-based canvases

**Decision:** Canvases without both `width` and `height` (e.g., audio-only canvases that carry only `duration`) are silently skipped — they cannot carry spatial text artefacts and are not an error. Canvases with `width`, `height` **and** `duration` (e.g., video content) are included normally: ALTO text processing is technically valid for them (unusual but not impossible — e.g., a film of a static wall of text).

**Future:** VTT and other time-based text formats will be supported later via the `ITextFormatProvider` extensibility point, at which point time-only canvases will be revisited.

---

## 2026-03-29 — PR 6: Full job processing pipeline

### Concurrent ALTO fetching

ALTO files are fetched concurrently using `Task.WhenAll` bounded by a `SemaphoreSlim`. Results are held in an ordered array and fed to `TextBuilder` sequentially — the global line counter requires canvases in original order.

`IHttpClientFactory` is the correct tool: it pools the underlying `HttpMessageHandler` so TCP connections and HTTP/2 streams are reused even when multiple `HttpClient` instances are created. The apparent "create per call" pattern is intentional and idiomatic.

Default concurrency is 8, configurable via `TextServicesOptions.MaxConcurrentAltoFetches`.

**Future work (noted in code TODOs):** The right limit depends on where the ALTO files live — third-party HTTP needs to be low (4–8) for politeness; S3 same-region can be much higher (64–128). When the S3 storage implementation is added, consider auto-deriving the limit from the URI scheme/host, or adding a per-host override table to `TextServicesOptions`.

### Accept header for ALTO fetches

The Alto `HttpClient` sends `Accept: */*`. Sending specific XML media types (`application/xml`, `text/xml`) caused problems because many IIIF implementations return ALTO with `Content-Type: text/plain`, `application/octet-stream`, or nothing at all. We parse whatever comes back as XML regardless, so a permissive Accept header is the correct choice.

### Per-page vs whole-job error handling

Per-page ALTO failures (network errors, parse errors, 404) are caught and accumulated as warnings in `job.Errors`; the job continues and reaches `Completed`. HTTP 404 on an ALTO URI is treated as a sparse page (same as `Text = null`) rather than an error. Only manifest-fetch or storage failures set `Status = Failed`.

---

## 2026-03-29 — PR 7: Search API (IIIF Search v1 + Autocomplete v1)

### IIIF Search v1 response design

The `SearchHandler` returns a `sc:AnnotationList` with two parallel arrays:

- **`resources`** — one `oa:Annotation` per matched word rectangle, with `on: "{canvasId}#xywh=X,Y,W,H"` and an `@id` that embeds the coordinates for stable reference.
- **`hits`** — one `search:Hit` per logical match group (where a multi-word phrase produces multiple rects that are grouped by hit number). Each hit carries `match`, `before`, and `after` context strings (150 raw characters, skipped when ≥100 results — same threshold as the reference implementations). `annotations` is an array of the annotation `@id` values belonging to that hit.

The `within` object carries `total` (total hit count) per the v1 spec.

The `ignored` field is populated (and returned in the response) for any IIIF Search v1 parameters that are recognised but not processed: `motivation`, `date`, `user`, `box`. This is spec-compliant — the Search API must declare which params it silently ignores.

Annotation `@id` format: `{selfUrl}/anno/h{hitNumber}i{imageIndex}-{X},{Y},{W},{H}` — stable, encodes coordinates and page, unique within a response.

### Autocomplete response design

`AutocompleteHandler` returns a `search:TermList`. Queries shorter than 3 characters return an empty `Terms` list (not `null` / 404) — the resource exists, it's just empty for short prefixes. This matches the spec's intent and avoids 404 ambiguity.

Suggestions are returned ordered by length then alphabetically — consistent with the `AutoComplete.GetSuggestions` implementation in Core.

### In-process memory cache + AsyncKeyedLock

`TextCache` wraps `ITextStore` with `IMemoryCache` (sliding expiration, default 30 minutes). To prevent thundering-herd on cache misses under concurrent requests for the same key, `AsyncKeyedLocker<string>` (from `AsyncKeyedLock`) provides per-key async locking. The double-check pattern is used: check cache → acquire key lock → check cache again → load from storage. This ensures at most one storage load per key per expiry window regardless of concurrency.

### BaseUrl fallback

When `TextServices:BaseUrl` is empty in configuration, the self-URL falls back to `{ctx.Request.Scheme}://{ctx.Request.Host}`. This works correctly for direct access and for reverse proxies that set the `Host` header. Production deployments behind a proxy that changes the scheme (e.g., HTTP internally) should set `BaseUrl` explicitly.

### MediatR request/response pattern

Both Search and Autocomplete features use `IRequest<T?>` / `IRequestHandler<TRequest, TResponse>` via MediatR. The `ISender.Send` call in `Program.cs` returns `null` for a missing resource (404) or a populated response object. The HTTP layer is decoupled from the business logic, which is fully testable without a running HTTP server.

---

## 2026-03-29 — PR 8: Search API — text-augmented Manifest endpoint

### text-augmented design

`GET /text-augmented/v3/{**id}` loads the stored Manifest JSON from `ITextStore`, patches it in-place as a `JsonNode`, and returns the result. No IIIF object model is used — `System.Text.Json.Nodes.JsonNode` / `JsonObject` is sufficient for the two mutations required.

Two mutations are applied:
1. **`id` / `@id` replacement** — the manifest's own identifier is replaced with the text-augmented self URL, so IIIF clients that resolve the returned manifest get a stable, routable document. v3 manifests use `id`; v2 manifests use `@id`; both are handled.
2. **Search service injection** — an IIIF Search v1 service descriptor is inserted into the `service` array (with a nested autocomplete service descriptor inside it). The `service` field can be absent, a single object, or an array; all three cases are handled. The search service is inserted at **position 0** (not appended) so that IIIF clients that take the first search service they find will use ours, pushing any pre-existing services down.

### Manifest as plain JSON

The Search API does not parse the Manifest into an IIIF object model. The stored JSON is parsed directly into `JsonNode`. This is consistent with the Builder API's `ManifestReducer` approach and avoids an `iiif-net` dependency. The Manifest is treated as a passthrough document with two targeted patches.

### Manifest caching

The Manifest JSON is loaded directly from `ITextStore` on each request — it is not routed through `TextCache`. Manifests are much smaller than `Text` objects, are not queried at high frequency (typically fetched once per viewer session), and change rarely. Caching them would add complexity for marginal benefit; this can be revisited if load patterns change.

### Cache sizing — entry count not word count

During PR 7/8 development, the initial cache sizing used word count as the `IMemoryCache` size unit. This was identified as flawed: a `Text` object whose word count exceeds `SizeLimit` can never be admitted to the cache, silently degrading to uncached storage reads on every request. The correct unit is **entry count** (`Size = 1` per object, `SizeLimit = CacheMaxEntries`). Every Text is cacheable regardless of size; memory headroom is controlled at the infrastructure level (ECS task memory limit). See `instructions/cache-usage.md` for full analysis including LOH, ECS, and horizontal scaling considerations. Tracking issue: tomcrane/TextServices#9.

---

## 2026-03-29 — PR 9: Storage — S3 implementation

### S3TextStore design

`S3TextStore` implements `ITextStore` backed by AWS S3. It accepts an `IAmazonS3` instance directly (for testability), with a factory overload that builds the client from `S3TextStoreOptions` (bucket, key prefix, region). The same three fixed filenames per key (`text.bin`, `autocomplete.bin`, `manifest.json`) are used as in the filesystem implementation; the job key is the S3 key prefix, with an optional global prefix for namespace isolation. `Exists()` uses a lightweight `GetObjectMetadataAsync` check on `text.bin` — no data is transferred.

### S3 testing with FakeS3

`S3TextStore` is tested with a `FakeS3 : AmazonS3Client` stub that overrides only `PutObjectAsync`, `GetObjectAsync`, and `GetObjectMetadataAsync`. The in-memory dictionary keyed by bucket+key is sufficient to exercise the full contract. `AmazonS3Client` is subclassable because its request methods are virtual — no interface indirection is needed. `Moq` is deliberately not used.

---

## 2026-03-29 — PR 10: E2E tests

### Test approach: WebApplicationFactory, not Playwright

The E2E tests use `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<T>` to host both the Builder API and Search API in-process, without browser automation. This was chosen over Playwright because: (a) the public-facing surface is JSON APIs, not HTML pages; (b) in-process testing is faster, more deterministic, and avoids external service dependencies; (c) the CLAUDE.md suggestion of Playwright was for a future phase.

### Dual WebApplicationFactory setup

Two factories run simultaneously in a single `E2ETestContext : IDisposable`:
- `BuilderApiFactory : WebApplicationFactory<BuilderDbContext>` — uses `BuilderDbContext` as `TEntryPoint` to avoid ambiguity with the global `Program` class that also exists in the Search API project.
- `SearchApiFactory : WebApplicationFactory<TextCache>` — uses `TextCache` as `TEntryPoint` for the same reason.

Both factories share a single temp directory (`FileSystemTextStore`) created by `E2ETestContext`. Builder writes there; Search reads from the same path.

### In-memory infrastructure replacements

All external infrastructure is replaced in `ConfigureTestServices`:
- **EF Core PostgreSQL → InMemory**: removes all DI descriptors whose service type is generic over `BuilderDbContext` (including `IDbContextOptionsConfiguration<BuilderDbContext>` — not just `DbContextOptions<BuilderDbContext>`), then adds `AddDbContext` with `UseInMemoryDatabase`. Removing only `DbContextOptions<T>` is insufficient: EF Core rebuilds options by applying all registered `IDbContextOptionsConfiguration<T>` actions, so both Npgsql and InMemory end up registered and EF Core throws. The fake connection string `"Host=test-placeholder;"` prevents `Program.cs` from throwing at startup before the override takes effect.
- **Hangfire PostgreSQL → InMemory**: `AddHangfire(config => config.UseInMemoryStorage())` — `Hangfire.InMemory` replaces the existing PostgreSQL storage without needing to remove the prior registration.
- **ITextStore**: replaced with `FileSystemTextStore` pointing at the shared temp dir.
- **IAltoFetcher → FixtureAltoFetcher**: maps real Wellcome ALTO URLs to local XML files under `Fixtures/b2888193x/alto/`.
- **IManifestFetcher → FixtureManifestFetcher**: maps real Wellcome manifest URLs to local JSON files under `Fixtures/{bnumber}/manifest.json`.

### Fixture strategy

Real Wellcome fixture files are checked into `src/TextServices.Tests.E2E/Fixtures/`. The main fixture is `b2888193x` (16 canvases, all with ALTO). A synthetic `b28770997` manifest (3 canvases, no `seeAlso` links) is used to test the zero-word path. The real b28770997 is a IIIF Collection (multi-volume), not a Manifest, and cannot be used directly.

### Job polling helper

`E2ETestContext.WaitForJobAsync` polls `GET /textbuilder/{id}` until the response body contains `"Completed"` or `"Failed"`, with configurable timeout (default 30 s) and incremental backoff (200 ms, growing to 2 s).

<!-- Add new sessions below this line -->

---

## 2026-03-31 — Plain text derivative (PR #22)

### Plain text stored separately from Text protobuf

**Decision:** `rawtext.txt` is written alongside `text.bin` rather than extracted from the
protobuf at request time.

Two reasons: (1) it is small, so storage cost is negligible; (2) plain text is the kind of
content that will be bulk-harvested, where requiring deserialization of the full `Text` object
on every request would quickly exhaust memory and degrade throughput.

`ITextStore` gains `SaveRawText` / `LoadRawText`. `FileSystemTextStore` stores as `rawtext.txt`;
`S3TextStore` stores with `Content-Type: text/plain`.

### `GET /text/v1/{**id}` endpoint

New Search API endpoint serving `rawtext.txt` as `text/plain`. Handler loads directly from
`ITextStore` (no in-process cache — the file is a plain string, not a large binary, and is
served cheaply via file read or S3 GET).

### Rendering link — unconditional

The plain text rendering link is added to `manifest.rendering[0]` in the text-augmented manifest
unconditionally whenever `textStore.Exists(id)` is true. The same policy is applied to the later
PDF derivative: the link appears immediately once text artefacts exist, even before a consumer has
triggered generation. The first request for a not-yet-generated derivative may be slow; subsequent
requests serve from storage.

---

## 2026-03-31 — PDF derivative design session

### Library choice: iText 7 Community (AGPL)

iText 7 Community is the only viable open-source .NET library with native support for PDF text
rendering mode 3 (Tr=3 — invisible text). PdfSharp (MIT) has no text rendering mode API; making
it work would require injecting raw PDF content-stream operators through unsupported internal
paths. All other capable options (Aspose, Syncfusion, IronPDF) are commercial.

The project is open source on GitHub, so AGPL is compatible. iText usage is isolated in a
dedicated `TextServices.Pdf` project so the AGPL dependency does not contaminate Core, Storage,
or the API projects.

### On-demand generation, not build-time

PDF generation requires fetching all page images — potentially 100s of HTTP requests for a large
manifest. Doing this during the Builder API job would make job times unpredictable and couple the
job's success to the availability of every image server. Instead, the PDF is generated lazily on
first request by the Search API and cached to storage.

### Synchronous GET + async POST

- `GET /pdf/v1/{**id}` — synchronous; generates if absent, serves on completion. This is the
  link in `rendering`. Always eventually returns a PDF (or 404 if no text artefacts exist).
- `POST /pdf/v1/{**id}` — async trigger for machine-to-machine / bulk pre-generation workflows.
  Returns 202 Accepted with `Location` and `Retry-After`. A client that doesn't want to hold a
  long-lived HTTP connection POSTs to trigger generation, does other work, then GETs when ready.

The GET always blocks until complete regardless of whether a POST triggered background
generation — the `AsyncKeyedLock` on the job key serialises concurrent requests automatically.
Streaming the PDF during generation provides no UX benefit because PDF viewers cannot render
anything until the complete file is received (the cross-reference table is at the end).

### Rendering link — unconditional (same policy as plain text)

The PDF rendering link is added unconditionally to `manifest.rendering[0]` whenever
`textStore.Exists(id)` is true. PDF before plain text (matching Wellcome order). The first click
may be slow on a cold cache; all subsequent clicks serve from storage.

### Image sizing

Prefer the painting annotation body's own `width`/`height` over canvas dimensions when
evaluating the 2000 px longest-edge threshold (canvas dimensions may be at a different scale
than the image resource). Use the image service `sizes` array when available (pre-generated
tiles, most cache-friendly). Fall back to `!2000,2000` IIIF Image API request as last resort.

### Page sizing and coordinate mapping

PDF page sized at **150 dpi** from the fetched image's actual pixel dimensions (not canvas).
Words from `Text.Words` are filtered by `Word.Idx == pageIndex`. Y-axis is flipped (PDF origin
bottom-left). Horizontal scale (`Tz`) stretches each word string to fill its bounding box width,
the standard OCR-overlay technique.

Full design in `instructions/pdf-derivative.md`.

### Implementation notes — bugs found during development

**iText 9 image placement**: `AddImageFittedIntoRectangle` is unreliable in iText 9.x and
produced blank pages. Replaced with `AddImageWithTransformationMatrix(imageData, w, 0, 0, h, 0, 0)`
which directly sets the PDF CTM and works correctly.

**User-Agent required by IIIF image servers**: The Wellcome image server (and likely others)
returns HTTP 403 for requests without a `User-Agent` header. .NET `HttpClient` sends no
`User-Agent` by default. Fixed by configuring the "pdf" named `HttpClient` with a descriptive
agent string (`TextServices/1.0 +https://github.com/tomcrane/TextServices`).

**iText closes the output stream on document close**: `PdfDocument.Close()` closes the
underlying stream, which disposes the `MemoryStream` before `SavePdf` can read it. Fixed with a
`NonClosingStream` decorator that forwards all operations except `Close()`/`Dispose()`.

### `.pdf` URL extension

The `GET /pdf/v1/{**id}` route accepts an optional `.pdf` suffix
(e.g. `/pdf/v1/my/book.pdf`) which is stripped before the key lookup. This makes browser
"Save As" filenames friendlier without requiring a separate route or route constraint.
