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

## 2026-04-19 — PR #32: Manifest-level annotations derivative; open CORS; response compression

### Manifest-level `AnnotationPage` stored at build time

A manifest-level `AnnotationPage` (all canvases, line granularity) is now generated by `TextBuildJob` and stored as `annotations.json` at the end of every build.

**Why stored at build time rather than generated on demand (contrast with per-canvas line/word endpoints from PR #29):**

The per-canvas line/word endpoints (`/annotations/lines/v1/{n}/{**id}`, `/annotations/words/v1/{n}/{**id}`) are generated on demand from the cached `Text` object because each canvas is a small slice of that object. The manifest-level page is the union of all canvases — for a 400-canvas manuscript it can be large enough that materialising and serialising it on every request is wasteful. Storing it once at build time and serving the pre-built JSON is cheaper for the common harvesting use case.

**URL structure:** `GET /annotations/manifest/v1/{**id}`. Follows the same pattern as `/annotations/lines/v1/{n}/{**id}` but without the canvas-index segment, since this endpoint spans all canvases.

**ID patching at serve time:** Annotation `id` values are stored with relative paths (e.g. `"anno/0"`) and prefixed with the serving URL by `ManifestAnnotationsHandler` — the same pattern used by `FiguresHandler`. This means the stored JSON is base-URL-agnostic.

**Text-augmented manifest:** A reference to the manifest annotations page is injected into the manifest-level `annotations` array (the top-level `annotations` field on the Manifest, not per-canvas). The ordering places the manifest annotations reference before the figures reference.

**Temporal support:** Temporal canvases produce `#t=start,end` targets in the stored page; spatial canvases produce `#xywh=` targets. The `Sec()` helper formats milliseconds to decimal seconds (up to 3 places, no trailing zeros), consistent with the per-canvas annotation endpoints.

### CORS: unconditional `Access-Control-Allow-Origin: *`

The previous CORS configuration allowed a configurable `CorsAllowedOrigins` list. This has been removed and replaced with unconditional `AllowAnyOrigin()`. Rationale: the IIIF Content Search specification and IIIF Presentation 3 specification both require `Access-Control-Allow-Origin: *` on all responses. The Search API is entirely public and read-only; there is no credential or session to protect. A configurable allowlist was technically correct HTTP but wrong for IIIF — any viewer from any origin must be able to call these endpoints without CORS negotiation. `CorsAllowedOrigins` removed from `appsettings.json` and docs.

### Response compression extended to `application/json`

`UseResponseCompression()` was already available in ASP.NET but the default MIME type list excludes `application/json` and `application/ld+json`. Those types are now explicitly added, so large search responses and text-augmented manifests are Brotli/Gzip compressed. A `BrotliCompressionProvider` is added alongside the default `GzipCompressionProvider`. Both are set to `CompressionLevel.Fastest` — the Search API is latency-sensitive; CPU is the cheap resource, not bandwidth. `EnableForHttps = true` is required because ASP.NET disables compression over HTTPS by default (BREACH mitigation); the Search API responses are not vulnerable to BREACH (no user secrets, no cross-origin injection).

---

## 2026-03-30 — Demo UI (PR #17)

### New project: TextServices.Demo

A fourth ASP.NET 10 application (`TextServices.Demo`) was added as a demonstrator and development tool. It is a static-file host serving vanilla ES2022 modules — no build pipeline, no framework. Three pages:

- **`/builder`** — submit manifests, list/poll jobs (localStorage job tracking + paged list API)
- **`/viewer`** — IIIF v3 viewer with search panel, autocomplete (datalist), and hit overlays
- **`/compare`** — side-by-side search comparator for two services; parallel fetch and latency display

A shared `iiif-helpers.js` module centralises: config loading, manifest fetching (v3 only), canvas/service extraction, IIIF Image API URL building, and xywh fragment parsing. Service URLs are injected via a `GET /demo-config` endpoint on the Demo app so the JS has no hardcoded API addresses.

### CORS

Both Builder and Search APIs gained CORS middleware permitting `http://localhost:5100` (the Demo app's dev port) in development configuration.

---

## 2026-03-30 — List jobs endpoint (PR #16)

### GET /textbuilder — paged list

A `PagedResult<T>` wrapper carries `page`, `pageSize`, `totalCount`, and `items`. `pageSize` is clamped to 1–100; `page` is clamped to a minimum of 1. Jobs are returned ordered newest-first with an optional `status` query-string filter. No significant design debate — straightforward extension of the existing CRUD surface.

---

## 2026-03-30 — IIIF Search API v2 (PR #18)

### Response shape

The v2 response is a W3C Web Annotation `AnnotationPage` (not `sc:AnnotationList`). Key differences from v1:
- Uses `id`/`type` throughout (no `@id`/`@type`/`profile`).
- Each annotation is a `PaintingAnnotationV2` with a `TextualBody` and a `target` pointing to `{canvasId}#xywh=...`.
- Context strings are in a `ContextualizingAnnotation` with a `TextQuoteSelector` (`exact`, `prefix`, `suffix`) rather than inline `hits[].before`/`after` fields.
- Autocomplete returns a `TermPage` with `items[].value` rather than a `TermList` with `terms[].match`.

### Service descriptors — Presentation 3 style

The text-augmented manifest endpoint was found to be emitting v2-style service descriptors (`@id`/`@type`/`profile`) — the Presentation 2 legacy format. Corrected:
- v2 services use `id`/`type` = `SearchService2` / `AutoCompleteService2`.
- v1 services use `id`/`type` = `SearchService1` / `AutoCompleteService1`.
- No `@context` inside individual service blocks.
- v2 service is listed first; v1 second — forward-looking default, backward-compatible fallback.
- Nested autocomplete service is an array, per Presentation 3 spec.

### Demo UI dual-format support

`iiif-helpers.js` gained `normalizeSearchResults` (auto-detects v1 `resources/hits` vs v2 `items`) and `normalizeAutocompleteTerms` (handles both `terms[].match` and `items[].value`). Both viewer and comparator use the shared normalizers.

---

## 2026-03-30 — Reprocess endpoint and N-column comparator (PR #19)

### PUT /textbuilder/{**id}

Resets a `Completed` or `Failed` job back to `Waiting` and re-enqueues it in Hangfire. Returns `409 Conflict` if the job is currently `Running` (the worker may not be safely interruptible mid-flight). The stale Hangfire job entry is cancelled before the new one is enqueued to avoid two concurrent executions.

### JobResponse — search URLs added for v2

`JobResponse` was only populating `SearchV1`/`AutocompleteV1` URLs; `SearchV2`/`AutocompleteV2` were always null. Fixed to populate all four for `Completed` jobs.

### N-column comparator

The comparator was originally hardcoded to exactly two columns (a/b). Text-augmented manifests now carry three services (SearchService2, SearchService1, plus any pre-existing service), and any number is valid. Columns are now built dynamically from the services found in the manifest; the grid template is set from JS (`repeat(N, minmax(200px, 1fr))`).

---

## 2026-03-30 — Identified Figures feature (PR #20)

### Storage

`ITextStore` gained `SaveFigures`/`LoadFigures`. The figures derivative is stored as `figures.json` — a IIIF AnnotationPage JSON string. This parallels `rawtext.txt` (separate derivative, separate store method) and keeps `text.bin` unchanged.

`FileSystemTextStore` and `S3TextStore` both implement the new methods. `S3TextStore` stores with `Content-Type: application/json`.

### Generation at build time

The IIIF AnnotationPage for identified figures is generated by `TextBuildJob` immediately after the text build completes (same job, no separate trigger). Rationale: ComposedBlock data is already in memory at that point; generating the derivative eagerly costs negligible extra time and avoids a separate lazy-generation path.

### ID patching at serve time

The stored AnnotationPage uses a placeholder base URL for annotation and resource IDs. `FiguresHandler` patches them to the actual serving URL at request time — the same pattern used for the text-augmented manifest. This avoids re-storing the file if the base URL changes.

### ComposedBlock scan scope

Initial implementation scanned only `<ComposedBlock>` elements with `TYPE="Table"` or `TYPE="Illustration"`. Found that ALTO files also use `<IllustratedElement>` and `<GraphicalElement>` for non-text regions not wrapped in `ComposedBlock`. Fixed to also scan those elements. Prefer our generated annotation over any annotation the manifest itself carries for the same canvas region.

---

## 2026-03-30 — hOCR text format provider (PR #21)

### Provider design

`HocrTextFormatProvider` implements `ITextFormatProvider` (XML-based, same as ALTO). hOCR is well-formed XHTML so `XElement` parsing works without a separate interface. Handles:
- XHTML namespace (elements may or may not carry `http://www.w3.org/1999/xhtml`).
- Both `ocrx_word` (Tesseract) and `ocr_word` class names.
- All non-text figure classes: `ocr_figure`, `ocr_graphic`, `ocr_linedrawing`, `ocr_photo` → `Illustration`; `ocr_table` → `Table`.
- Coordinate scaling from the `ocr_page` bbox to canvas dimensions (same rescaling logic as ALTO).

### AltoTextFormatProvider as default fallback

`AltoTextFormatProvider.Supports(null, null)` now returns `true` — making it the fallback provider for canvases where no format metadata is present (e.g., inline `sourceData` entries). This was previously implicit (the only provider always processed everything); now it is an explicit and tested contract.

### PageInstruction extended

`PageInstruction` gained `Profile` and `Label` properties to carry `seeAlso` metadata from `ManifestReducer` through to `TextBuilder.AddPage`, enabling the provider selection logic (`ITextFormatProvider.Supports(profile, label)`) to work correctly per canvas.

### ManifestReducer refactor

`FindAltoUri` was renamed `FindTextSource`, returning a `(Uri, Profile, Label)` record. `IsRecognisedTextFormat` was extended to detect hOCR profiles and labels alongside ALTO. All call sites updated.

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

---

## 2026-04-02 — VTT temporal search (PR #25)

### Core insight: the search engine is coordinate-agnostic

`Text.Search()` operates entirely on flat strings (`NormalisedFullText`) and a position index (`Words` keyed by character offset). It knows nothing about pixels or time. The same `IndexOf` + coalesce-by-`Li` algorithm works for temporal content without any changes. `AutoComplete` is similarly unaffected.

### Model changes

**`Word`** gains `StartMs`/`EndMs` (ProtoMembers 13/14, integer milliseconds). Spatial words carry `X/Y/W/H` and leave `StartMs`/`EndMs` = 0; temporal words carry `StartMs`/`EndMs` and leave `X=Y=W=H=0`. Milliseconds (not seconds) in the model keeps them as integers in the protobuf; division to seconds happens only at response-building time.

**`Image`** gains `IsTemporalContent` (ProtoMember 3, bool, default false). This is a per-canvas flag — mixed manifests (some image canvases with ALTO, some video canvases with VTT) are correctly handled because each canvas records its own coordinate type in its `Images[]` entry.

**`ResultRect`** gains non-persisted `StartMs`/`EndMs`. During coalescing, `Min(StartMs)` / `Max(EndMs)` is taken alongside the existing spatial merging. For spatial words `StartMs = EndMs = 0` throughout, so the min/max is a no-op; for temporal words `X = W = 0` throughout, so the existing `W = (next.X + next.W) - current.X` evaluates to 0 — both paths are safe without special-casing.

All three changes are backward-compatible: existing serialised files default new fields to 0/false.

### ITranscriptFormatProvider — separate interface for text-based formats

VTT is not XML. Rather than change `ITextFormatProvider` (breaking the ALTO/hOCR contract), a second interface `ITranscriptFormatProvider` was added, taking `string rawContent` instead of `XElement`. `TextBuilder` supports both provider lists; `AddPage` routes XML formats; `AddTranscriptPage` routes text-based formats.

### VttTextFormatProvider — word granularity, not cue granularity

Words within a VTT cue are indexed individually (not as a single token per cue). Reasons: (a) multi-word phrase queries work correctly within a cue using the existing positional logic; (b) cross-cue matches produce separate `ResultRect`s each with their own time annotation — which is the correct IIIF Search v2 behaviour; (c) it mirrors the ALTO approach. All words in a cue share the same `StartMs`/`EndMs` and the same `Li` (one `NextLine()` call per cue). Multi-line cue text is concatenated before word-splitting — `NextLine()` is not called per text line.

### Text-source detection — two locations

Text resources (ALTO, hOCR, VTT) can appear on a canvas in:
1. `seeAlso` — the established location for ALTO and hOCR.
2. `canvas.annotations[]` with `motivation: supplementing` — the IIIF v3 pattern for captions/transcripts, used for VTT.

`ManifestReducer.FindTextSource` tries `seeAlso` first, then falls back to supplementing annotations. Either location can carry any recognised format. `motivation` may be a string or an array — a `HasSupplementingMotivation` helper handles both. Externally-referenced AnnotationPages (those with only an `id`, no embedded `items`) are silently skipped.

### Format detection extended to three signals

`IsRecognisedTextFormat` now accepts `profile`, `format`, and `label` as independent signals (previously only `profile` and `label`). The `format` field is now extracted from `seeAlso` entries and annotation bodies and carried through `TextSource` → `PageInstruction`. VTT terms: `"text/vtt"`, `"vtt"`, `"webvtt"`, `"transcript"` (all case-insensitive, checked in both profile and format fields, plus label).

### Temporal canvas inclusion

VTT canvases commonly have `duration` but no `width`/`height`. The canvas skip logic was extended: duration-only canvases are included with `Width = 0, Height = 0` when a VTT text source is detected. Audio-only or dimensionless canvases without a VTT source are still silently skipped.

### Search API v2 — temporal fragment emission

`SearchV2Query.BuildTarget` checks `text.Images[rect.Idx].IsTemporalContent`:
- `false` → `{canvasId}#xywh=X,Y,W,H` (unchanged)
- `true` → `{canvasId}#t=start,end` (seconds, `CultureInfo.InvariantCulture` to avoid locale-dependent decimal separators)

`motivation` is set to `"supplementing"` for temporal annotations, `"painting"` for spatial. Annotation IDs for temporal results use the format `.../anno/h{Hit}i{Idx}-t{StartMs},{EndMs}` to remain unique and stable within a response.

**Search API v1** is not updated for temporal content. It would emit `#xywh=0,0,0,0` for temporal results — a known limitation. VTT content is only correctly surfaced via the v2 handler.

### VTT detection term duplication — conscious tradeoff

The same detection terms appear in `ManifestReducer.IsVttFormat`, `TextBuildJob.IsVttPage`, and `VttTextFormatProvider.Supports`. This is three small static helpers rather than a shared utility dependency. Noted in the implementation plan: if a fourth location appears, extract to a `TextFormatDetection` static class in `TextServices.Core.Providers`.

### Demo viewer — temporal canvas support

The Demo viewer detects whether a canvas carries a Video/Sound painting body and switches between the image viewer and a `<video>` player. `navigateToHit` seeks and plays video for temporal hits; search results show a formatted timestamp badge (MM:SS / H:MM:SS) instead of spatial overlays. A bug was found: calling `play()` immediately after setting `currentTime` races with the async seek and starts playback from position 0. Fixed by waiting for the `seeked` event before calling `play()`.

### Tests

326 tests total (35 new), all passing. New files: `VttParsingTests.cs` (15 tests — provider unit tests), `SearchV2HandlerTests.cs` (9 tests — temporal and spatial annotation shape). Additions to `ManifestReducerTests.cs` (6 tests — VTT detection via both seeAlso and supplementing annotations), `TextBuildJobTests.cs` (3 tests — VTT fetch and build paths), `ProtobufSerializationTests.cs` (2 tests — round-trip guards for new fields).

---

## 2026-04-16 — Conditional PDF rendering link (PR #26)

### Problem

`TextAugmentedQuery` was unconditionally inserting a PDF rendering link whenever text artefacts existed for a given id. For temporal-only manifests (video/audio with VTT transcripts), the PDF derivative has no meaning: there are no page images to render. The PDF link was appearing in the text-augmented manifest for such resources even though following it would either fail or produce an empty document.

### Fix

`TextAugmentedHandler` now injects `ITextCache` alongside the existing `ITextStore`. When adding rendering links, it loads the `Text` object and checks `text.Images.Any(img => !img.IsTemporalContent)`. The PDF link is only added when at least one canvas is spatial (image-based). The plain text link is added unconditionally — a plain text transcript is valid for both spatial and temporal content.

`ITextCache` is already used by the search and autocomplete handlers. Loading `Text` here is cheap in practice because the cache means the protobuf is only deserialised once per expiry window regardless of how many requests hit the endpoint. The `Images` array is small (one entry per canvas) so the check itself is negligible.

**Mixed manifests** (some image canvases, some video canvases in the same `Text`) are handled correctly: `Any(img => !img.IsTemporalContent)` returns true if any canvas is spatial, so a PDF link is included. The PDF generator already skips canvases with no image body, so the output is correct.

### Tests

The existing `Handle_TextExists_AddsPdfAndPlainTextRenderingLinks` test was split into two:
- `Handle_ImageBasedContent_AddsPdfAndPlainTextRenderingLinks` — spatial `Text` → both links present
- `Handle_TemporalOnlyContent_AddsPlainTextButNoPdfLink` — temporal-only `Text` → plain text only

Two further tests cover the "prepended to existing rendering" variants. `StubTextCache` added to the test helpers; `MakeHandler` now takes `Text? cachedText` instead of `bool textExists`. `SpatialText()` and `TemporalText()` helpers build minimal `Text` objects via `TextAccumulator` directly. 328 tests total, all passing.

---

## 2026-04-16 — W3C Annotation text source provider (PR #27)

### Problem

When a IIIF Manifest has no ALTO, hOCR, or VTT text source, there may still be text available via W3C Web Annotations — supplementing `AnnotationPage`s linked from `canvas.annotations`, where each annotation carries a `TextualBody` value and a spatial (`#xywh=`) or temporal (`#t=`) target. These are the only text sources for many Wellcome manifests and are common in the wider IIIF ecosystem.

### Multi-word annotations are identical to VTT in structure

As in VTT (where all words in a cue share the same time range), all words within one W3C annotation share the same bounding box. This maps cleanly onto the existing model: each word gets `X/Y/W/H` set to the annotation's xywh, and the same `Li` value. Coalescing in `GetRectangles` then works correctly: a single-word query returns the annotation's box; a phrase query spanning one annotation collapses to the same box; a phrase spanning two annotations produces two separate rects. No rescaling is needed — annotation target coordinates are already in canvas pixel space.

### `ITranscriptFormatProvider` renamed `IStringFormatProvider`

The interface was originally named for transcripts (VTT), but `W3cAnnotationTextFormatProvider` is also spatial. The interface's defining characteristic is that it takes a raw string rather than an `XElement`. Renamed to reflect this. `ITranscriptFormatProvider.cs` is kept as a comment-only file; all implementations and usages updated.

### Target parsing — two forms

Both URI fragment targets (`canvasId#xywh=x,y,w,h`) and SpecificResource/FragmentSelector objects are handled:

```json
{ "type": "SpecificResource", "source": "canvasId",
  "selector": { "type": "FragmentSelector", "value": "xywh=0,0,750,300" } }
```

The `value` field in `FragmentSelector` is the fragment *without* the `#`. Both `xywh=` (spatial, optional `pixel:` prefix) and `t=` (temporal, decimal seconds → milliseconds) are supported. `percent:` coordinates are not supported and silently skip the annotation.

For temporal annotations (`#t=`), `CultureInfo.InvariantCulture` is used for decimal parsing, consistent with the VTT and Search API v2 temporal handlers.

### Text source detection — third fallback in `ManifestReducer`

`FindTextSource` gains a third step after seeAlso and embedded supplementing annotations:

1. `seeAlso` → recognised format → return
2. `canvas.annotations` with embedded `items` → supplementing annotation with recognised format body → return
3. `canvas.annotations` with only an `id` (no `items`) → externally-referenced `AnnotationPage` → return with sentinel format `"iiif-annotation-page"`

The sentinel value is defined as a constant on `W3cAnnotationTextFormatProvider` so it is shared between `ManifestReducer`, `TextBuildJob`, and the provider's `Supports` method. `seeAlso` still takes priority — a manifest with both ALTO seeAlso and line annotations will use ALTO.

Embedded `AnnotationPage`s that already have `items` are skipped by step 3 (they were already handled or skipped in step 2). This prevents double-processing.

### `FetchedPage.Vtt` renamed `StringContent`

The private record in `TextBuildJob` now carries `StringContent` instead of `Vtt`. Both VTT text and AnnotationPage JSON are plain strings routed to `AddTranscriptPage`; the correct provider is selected from `page.Format`. The renaming makes the intent clear.

### `isTemporalContent` flag

The `W3cAnnotationTextFormatProvider` collects all annotations in a first pass, determines `isTemporalContent` from the first annotation's target type, then calls `BeginPage` with that flag before processing words. Mixed spatial/temporal annotations on a single canvas are not expected in practice; if they occur, the first annotation's type wins.

### Tests

20 new tests in `W3cAnnotationParsingTests.cs`: provider `Supports()`, URI fragment and SpecificResource spatial targets, pixel: prefix, temporal URI fragment and SpecificResource, all-words-share-bounding-box, multi-annotation different Li, search integration (single word, phrase within annotation, phrase spanning annotations), ManifestReducer detection (external page, seeAlso priority, embedded items skipped).

---

## 2026-04-16 — PR #28: API documentation

### Context

Added consumer-facing documentation to the repository.

### What was added

**`README.md`** — top-level overview: what the two services do, a plain-ASCII architecture diagram showing the call flow from caller through Builder API to Search API, and a three-step quick start (POST a job, poll for completion, search, load decorated Manifest in a viewer). Links to the two detail pages.

**`docs/builder-api.md`** — full Builder API reference covering all five endpoints (`POST`, `GET` list, `GET` single, `PUT` reprocess, `DELETE`), the job response field table, `sourceData` format (for callers supplying an explicit page sequence rather than a Manifest URI), the three-step text source detection priority (seeAlso → embedded annotations → external AnnotationPage), recognised format table, and configuration reference.

**`docs/search-api.md`** — full Search API reference covering all endpoints (search v2/v1, autocomplete v2/v1, text-augmented Manifest, plain text, PDF GET and POST trigger, identified figures), example response bodies, a section on how to link the search services into a caller's own Manifest rather than using the text-augmented proxy, and configuration reference.

### Decision: plain ASCII diagrams only

The initial README used Unicode box-drawing characters (`│`, `─`, `►`, `◄`) for the architecture diagram. These rendered inconsistently across editors and terminals, causing misalignment. Replaced with plain ASCII (`|`, `-`, `>`, `<`) throughout.

---

## 2026-04-16 — PR #29: Text granularity annotation endpoints

### Context

Added two new Search API endpoints that return IIIF Presentation 3 `AnnotationPage` objects at line and word granularity, using the [IIIF Text Granularity extension](https://iiif.io/api/extension/text-granularity/). The augmented Manifest endpoint was also updated to inject per-canvas references to these pages.

### On-the-fly generation from the cached Text object

The annotation pages are generated on demand from the `Text` object already held in the Search API memory cache. No additional storage files are written during the build. Rationale:

- The `Text` object is already in memory for any manifest that has had a recent search request.
- Generating an `AnnotationPage` is a linear pass over `Text.Words` filtered by `Word.Idx` (canvas index), grouped by `Word.Li` (line), with bounding box unions — microseconds of CPU per request.
- Pre-generating all annotation pages during the build would write O(canvases × 2) extra files per job, adding build time and storage cost for content that most consumers will never request directly.
- The intended use case is **harvesting**: callers fetch the annotation pages once, rewrite their URLs, and serve them from their own infrastructure. A production deployment that should not serve harvesting traffic can disable the endpoints via configuration.

### URL structure: canvas index before job id

Route: `GET /annotations/lines/v1/{n:int}/{**id}` and `GET /annotations/words/v1/{n:int}/{**id}`

ASP.NET minimal API catch-all segments (`{**id}`) must be last in the route, so the canvas index `{n}` is placed before the job id. This gives URLs of the form `/annotations/lines/v1/0/my-collection/my-book`. The ordering is unconventional but unambiguous; harvesters are expected to rewrite URLs when serving annotation pages from their own systems.

### Per-canvas injection in text-augmented

The `/text-augmented/v3/{**id}` handler now iterates the manifest `items` array and prepends `AnnotationPage` references (lines and words) to each canvas's `annotations` array, for canvases that have at least one word. A `HashSet<int>` of occupied canvas indices is computed in a single O(words) pass before the canvas loop.

### Target format

Spatial canvases: `#xywh=x,y,w,h` where the line bounding box is the outer box of all word boxes on the line (min X/Y, max X+W / Y+H). Word boxes are used as-is.

Temporal canvases: `#t=start,end` in decimal seconds (up to 3 decimal places, no trailing zeros), where the line time range is the union of all word time ranges (min startMs, max endMs).

### Tests

24 new tests in `LineAnnotationsHandlerTests.cs` and `WordAnnotationsHandlerTests.cs`: 404 cases (text not found, canvas out of range, negative index), response shape (correct metadata, granularity label, contexts), single and multi-line spatial, outer bounding box calculation, per-word individual boxes, annotation ids, multi-canvas isolation, sparse canvas (empty items not 404), temporal time fragment formatting.

---

## 2026-04-16 — PR #30: Annotation comparator demo page

Added a new `/annotations` demo page that loads a text-augmented manifest and displays all annotation pages referenced in a canvas's `annotations` array as side-by-side columns. Source pages (from the original manifest) appear on the left; generated pages (our line/word transcriptions) appear on the right with a yellow "generated" badge. Each row shows body text, a spatial `#xywh=` or temporal `#t=` badge, and (for spatial canvases with an image service) an IIIF image crop thumbnail. Detection of "generated" vs "source" pages uses the presence of a `textGranularity` property on the fetched page, or a label containing "transcription".

An `index.html` landing page was also added as the application root, linking to all five demo tools.

---

## 2026-04-16 — PR #31: Fix cross-line hyphenation — merged word assigned to wrong line index

### Bug

In `AltoTextFormatProvider`, `accumulator.NextLine()` was called at the top of each `TextLine` loop before processing its strings. When a `HypPart1` word was pending from the previous line, the merge was completed *after* `NextLine()` had already advanced the `Li` counter. The merged word (e.g. "schwarzweiß") therefore carried the **second line's `Li`**, even though its bounding box coordinates came from the first line (the `HypPart1` position). This caused the line-level annotation endpoint to:

1. Omit the word from line N's annotation body (it was no longer on that `Li`).
2. Place the merged word at the start of line N+1's annotation body.
3. Produce a line N+1 bounding box whose x-origin started at the HypPart1 position (from the first line), spanning the visual area of both lines.

The discrepancy was discovered by comparing the Annotation Comparator's "source" and "generated" columns for a German-text manifest.

### Fix

When `hyphenPending` is true at the start of a new `TextLine`, the HypPart2 string is consumed and the merge is completed **before** `NextLine()` is called. The merged word is then emitted with the current (first-line) `Li`, and `startIdx = 1` ensures the HypPart2 string is skipped in the main per-string loop. A regression test (`AddPage_Hyphenation_MergedWord_AssignedToFirstLineIndex`) verifies that the merged word shares its `Li` with the other words on the HypPart1 line.

### Known limitation: body text and second-line start position

After the fix, the generated line annotation for a hyphenated line end shows the **merged** form in its body (e.g. "schwarzweiß"), while the source annotation shows only the raw first fragment ("schwarz¬"). This is correct and consistent with the existing design: the text index stores the merged form as a single word; no separate entry exists for the second fragment.

A side-effect is that the following line's annotation starts at the word **after** the HypPart2 fragment (e.g. "gemischt..."), not at the HypPart2 position ("weiß gemischt..."). The bounding box for that line correctly starts at the first remaining word. The source annotation, which is built from raw ALTO, includes "weiß" as the first word on the continuation line. This divergence between source and generated is **expected and not a bug** — it is a direct consequence of the word-merging design shared with both reference implementations.

---

## 2026-05-01 — imageUri semantics; /proxy/image endpoint; ManifestSynthesiser simplification

### imageUri is an already-resolved URL, not a IIIF Image API service root

The `imageUri` field in a `sourceData` page instruction is the `body.id` of the painting annotation
in the source Manifest — an already-resolved image URL chosen by the publisher. It is not assumed to
be a IIIF Image API service root. Concretely:

- **For `sourceUri` jobs**: `ManifestReducer` extracts `body.id` from the painting annotation and
  stores it as `imageUri`. The publisher has already chosen a display size (e.g. `656,1024` for the
  Wellcome b2888193x manifest) and we use it unchanged.
- **For `sourceData` jobs**: the caller supplies `imageUri` directly; it may be any fetchable image
  URL, a `file://` path, or an `s3://` URI.

**Why not construct IIIF Image API requests from the service root?** For `sourceUri` jobs we use
what the publisher has already chosen. For `sourceData` jobs we do not know whether the URI is a
service root or a direct image, and we have no reliable way to tell from the URI alone (a URL ending
in `.jp2` could be either). The correct approach is to treat every `imageUri` as a concrete URL and
pass it through unchanged. If the publisher wants a particular display size they express that in the
`imageUri` value they submit.

### ManifestSynthesiser — simplified URL resolution

The previous `BuildImageUrl` implementation attempted to construct a IIIF Image API request from
`imageUri` by computing a display size capped at 1024 px on the longer axis. This was removed.

`ResolveImageUrl` now has two cases only:

| Scheme | Result |
|---|---|
| `http` / `https` | Return `imageUri` unchanged |
| `file://` / `s3://` | Construct a `/proxy/image?uri=…` URL (requires `BuilderApiBaseUrl`) or return as-is |

### /proxy/image endpoint

`GET /proxy/image?uri={uri}` on the **Search API** allows IIIF viewers to load images from
synthesised Manifests when the `imageUri` is a `file://` path (local development fixtures).
The proxy lives on the Search API — not the Builder API — because the Search API is always running
when a viewer needs images; the Builder API may be shut down after jobs are processed. The endpoint:

- Accepts `file://` URIs → reads the file from disk via `IResourceFetcher` and streams it with the
  appropriate `Content-Type`.
- Accepts `s3://` URIs → returns a 1×1 transparent PNG placeholder (the S3 object is not fetched;
  this keeps the manifest structurally valid without requiring AWS credentials in the serving layer).
- Rejects all other schemes with `400 Bad Request`.

This is a local-dev tool. In production, `file://` and `s3://` `imageUri` values would not be used.

### SearchApiBaseUrl reused for proxy URL construction

`ManifestSynthesiser` uses the existing `SearchApiBaseUrl` (already in `TextServicesOptions` for
populating job response URLs) to construct proxy URLs for non-HTTP `imageUri` values. No new config
key is needed. When `SearchApiBaseUrl` is empty, non-HTTP URIs are stored as-is in the synthesised
Manifest (they will not load in a viewer but the text index is unaffected).

### AllowFileImageProxy security setting

A `bool AllowFileImageProxy` (default `false`) was added to both `TextServicesOptions` (Builder API)
and `SearchApiOptions` (Search API). It controls two things:

1. **ManifestSynthesiser**: when `false`, a `file://` imageUri causes the painting annotation to be
   omitted entirely from the synthesised manifest — no file path is embedded. When `true` and
   `SearchApiBaseUrl` is set, the annotation is present with a `/proxy/image` URL.
2. **Proxy endpoint**: when `false`, the Search API's `/proxy/image` endpoint returns the 1×1
   transparent PNG placeholder for `file://` URIs rather than serving the file contents — even if a
   proxy URL somehow ends up in a manifest on a deployment where proxying is disabled.

The default is `false` so that `file://` paths from local development are never accidentally embedded
in manifests or served from a production deployment. Both `appsettings.Development.json` files set
it to `true` to enable the full local-dev workflow. The S3 placeholder is unconditional: s3:// URIs
always return the placeholder regardless of this setting (S3 images are not proxied).

### Demo fixture paths computed server-side

The `sourcedata.js` demo page previously hardcoded `FIXTURE_ALTO` and `FIXTURE_IMAGES` as
`file:///C:/git/tomcrane/...` constants — checkout-specific and broken for anyone else. These are
now computed by the Demo app's `/demo-config` endpoint using `IWebHostEnvironment.ContentRootPath`
(the Demo project directory) and navigating up one level to `TextServices.Tests.E2E/Fixtures/b2888193x`.
The computed `file://` URIs are returned as `fixtureAlto` and `fixtureImages` in the config JSON.
If the fixture directory doesn't exist (e.g. a production deployment), both fields are `null` and
the JS template falls back to empty strings. No checkout-specific path is left in any source file.

---

## 2026-05-01 — Service bitmask, sourceData skeleton manifest, JobResponse URL fields

### Service bitmask (JobServices enum / capabilities.json)

A `services` integer field was added to the job request, job database record, and job response.
It is a bitmask of `JobServices` flags (defined in `TextServices.Storage`):

| Flag | Value | Controls |
|---|---|---|
| `Search` | 1 | IIIF Search v1/v2 endpoints |
| `Autocomplete` | 2 | Autocomplete endpoints |
| `FullText` | 4 | Plain-text endpoint |
| `Annotations` | 8 | Line/word and manifest annotation endpoints |
| `Pdf` | 16 | PDF endpoint |
| `TextAugmented` | 32 | Text-augmented manifest endpoint |
| `Figures` | 64 | Identified figures endpoint |

Default is `JobServices.All` (`-1` over the wire) so existing callers are unaffected.

**Build time**: `TextBuildJob` respects the flags when deciding which derivatives to build and store.
`Text` and `AutoComplete` are only written when their respective flags are set; similarly for
`RawText`, `Figures`, `Manifest`, and `AnnotationPage`. This avoids writing artefacts that will
never be served and lets callers express intent (e.g. "I only need search and autocomplete").

**Serve time**: when `services != All`, the Builder API writes a `capabilities.json` file containing
the integer bitmask. The Search API reads this via `ITextStore.LoadCapabilities` / `ITextCache.GetCapabilitiesAsync`
and returns `404 Not Found` for any endpoint whose flag is absent. Jobs with no `capabilities.json`
(pre-existing jobs, or jobs submitted with the default `All`) treat every service as enabled —
backward-compatible by design.

`CapabilitiesExtensions.IsEnabledAsync` is the single call site in every Search API handler; it
returns `true` when capabilities are null (no file = all enabled).

### sourceData skeleton manifest synthesis (ManifestSynthesiser)

When a job is submitted with `sourceData`, the Builder API synthesises a skeleton IIIF Presentation 3
Manifest from the page sequence and stores it alongside the text artefacts. This lets the
text-augmented, PDF, and annotation endpoints work identically for `sourceData` jobs and `sourceUri`
jobs — the same serving code reads the stored manifest in both cases.

**Why synthesise at build time, not serve time?** The synthesised manifest is stable: it is derived
entirely from the submitted page sequence. Synthesising it once at build time and serving it from
storage is simpler and cheaper than regenerating it on every text-augmented request.

`ManifestSynthesiser` uses iiif-net (`IIIF.Presentation`) to build the manifest as a typed object
and serialise it. This is the one place where iiif-net is used; the rest of the codebase continues
to treat manifests as plain JSON.

### JobResponse URL fields expanded

`JobResponse` previously exposed only `SearchV1`, `AutocompleteV1`, `SearchV2`, `AutocompleteV2`.
The following were added: `FullText`, `Pdf`, `TextAugmented`, `Annotations`, `Figures`. All are
populated from `SearchApiBaseUrl` when the job is `Completed` and the corresponding `JobServices`
flag is set. This gives callers a single document that describes every derivative endpoint without
them having to construct URLs themselves.

---

## 2026-05-01 — Fireball alignment: page types, PDF assembly, page sequence storage

### Context

Tom asked to align the `sourceData` page model with the
[Fireball](https://github.com/dlcs/fireball) PDF-assembly service payload format, adding
support for embedded PDFs and custom generated-text pages.

### Page types (`PageInstruction.Type`)

Three page types are now recognised:

| `type` value | Manifest canvas? | PDF page | Text indexed? |
|---|---|---|---|
| `null` / absent | Yes (with painting annotation if `imageUri` set) | Image + invisible text layer | Yes |
| `"pdf"` | **No** | Existing PDF embedded via iText `CopyPagesTo` | No |
| any other string | Yes (no painting annotation) | Centred message text | No |

> **Why allow pdf-type anywhere?** Fireball allows pdf embeds at any position. Restricting to
> first-only would have been an artificial constraint with no implementation benefit.

### `input` field

`input` serves dual purpose (matching Fireball):
- On `"pdf"`-type pages: the URI of the PDF to embed.
- On normal pages: an alias for `imageUri`. `imageUri` takes precedence when both are set.

No `method` field was needed (unlike Fireball's `method: "s3"/"download"`) because the existing
`IResourceFetcher` already dispatches on URI scheme.

### `title` and `customTypes` at job level

`title` is stored on the `BuilderJob` entity and used as PDF metadata / `Content-Disposition`
filename. `customTypes` is a `Dictionary<string, CustomPageType>` serialised to a JSON column
(`CustomTypesJson`). `CustomPageType.Message` is the centred text rendered on that page type in
the PDF.

An EF Core migration (`20260501120000_AddTitleAndCustomTypesColumns`) adds both nullable `text`
columns to the `Jobs` table.

### Page sequence storage artefact (`pagesequence.json`)

A `pagesequence.json` file is written to storage for `sourceData` jobs when the `Pdf` service
flag is set. It contains the full ordered sequence including pdf-embed entries (which have no
canvas in the synthesised Manifest) with messages inlined from `customTypes`. The PDF builder
reads this to reconstruct assembly order; the synthesised manifest alone is insufficient because
pdf-type pages are excluded from it.

```json
{
  "title": "My Book",
  "pages": [
    { "type": "pdf", "input": "file:///path/cover.pdf" },
    { "canvasId": "https://example.org/canvas/1" },
    { "type": "redacted", "canvasId": "https://example.org/canvas/2",
      "width": 3000, "height": 4000, "message": "This page has been redacted." },
    { "canvasId": "https://example.org/canvas/3" }
  ]
}
```

### PdfBuilder changes

`PdfBuilder.BuildAsync` now accepts an optional `pageSequenceJson` parameter. When supplied it
drives the full assembly loop; when null it falls back to the original `text.Images`-order loop
(sourceUri jobs). Three new helpers:

- `EmbedPdfAsync` — fetches the source PDF (`http/https` or `file://`) and copies all its pages
  into the output document.
- `AddTextPage` — adds a page with centred message text at A4 size (falls back to A4 if canvas
  dimensions are zero).
- `BuildCanvasIndexMap` — maps canvas IDs to their index in `text.Images` so words can be placed
  correctly even when the page sequence order differs from the text index order.

`textIndex == -1` (canvas present in manifest but absent from the text index, e.g. custom-type
pages) is handled gracefully — the image layer renders normally and the invisible text layer is
simply omitted.

### Demo UI update

The `sourcedata.js` template was updated to demonstrate all three page types: a pdf-type initial
page (embedding a `cover.pdf` from the fixture directory), two normal image pages, a redacted
custom-type page one before the end, and a final normal image page. `title` and `customTypes` were
added at the job level.

---

## 2026-05-01 — PDF page sizing: non-image pages now match image pages

### Problem

Custom-type pages (centred message text) and embedded PDF pages were sized from the canvas
dimensions recorded in `pagesequence.json` (e.g. 2495×4067 px at 150 dpi ≈ 1197×1952 pt). The
actual image pages, however, are sized from the pixel dimensions of the *fetched image*, which
for sourceData jobs is often a fixed-size URL (e.g. `full/656,1024/...` ≈ 315×492 pt). The
non-image pages were therefore roughly 2–4× too large.

The canvas dimensions and the fetched image dimensions can differ significantly because:
- `imageUri` is an already-resolved URL at a publisher-chosen display size, not the full-resolution asset.
- The synthesised manifest stores no `width`/`height` on the image body, so `pageInfo.ImageWidth`
  falls back to the canvas dimensions — which are always the full-resolution canvas size.

### Fix — image pages

`AddPageAsync` now returns `(float widthPt, float heightPt)` — the actual PDF page dimensions
computed from the fetched image's pixel dimensions. The caller tracks this as `refWidthPt /
refHeightPt` and updates it after every image page.

### Fix — custom-type pages

`AddTextPage` was changed to accept `float widthPt, float heightPt` directly (not canvas
dimensions). It receives the current `refWidthPt / refHeightPt`, so it always matches the most
recently rendered image page exactly.

### Fix — embedded PDF pages

`EmbedPdfAsync` was changed to accept `float targetWidthPt, float targetHeightPt` and now:
1. Imports each source page as a `PdfFormXObject` via `CopyAsFormXObject` (replaces `CopyPagesTo`).
2. Creates a new output page at the target size.
3. Scales the XObject to fit within that page, preserving aspect ratio (letterbox/pillarbox if
   aspect ratios differ), and centres it with `AddXObjectWithTransformationMatrix`.

### Pre-scan for reference size

Embedded PDFs can appear before any image page (the demo cover page is position 0). At that
point `refWidthPt / refHeightPt` is unknown. `DetermineReferenceSizeAsync` solves this:

- Only called when the page sequence contains at least one `"pdf"`-type entry.
- Walks entries, finds the first normal canvas, fetches its image, and returns the actual pt
  dimensions derived from the image's pixel dimensions.
- Used to initialise `refWidthPt / refHeightPt` before the main assembly loop.
- The first image is fetched twice (once in pre-scan, once in the main loop) — acceptable for a
  non-hot PDF generation path.

Falls back to A4 if no image page exists (edge case: a PDF consisting entirely of pdf-type and
custom-type pages).
