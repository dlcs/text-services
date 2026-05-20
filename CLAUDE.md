# TextServices — Claude Code Guide

## Project Overview

A standalone .NET 10 C# solution providing IIIF Text Services — specifically:
1. A **class library** for building `Text` search index objects from ALTO (and future text-segmentation) files.
2. A **Builder API** — async job-based HTTP API that accepts IIIF Manifests or simple page sequences and produces stored `Text` + `AutoComplete` artefacts.
3. A **Search API** — public-facing HTTP API providing IIIF Search v1 (and later v2) endpoints backed by those artefacts.

### Background reading
- METS-ALTO standard: https://www.loc.gov/standards/alto/
- IIIF Search API v1: https://iiif.io/api/search/1.0/
- IIIF Search API v2: https://iiif.io/api/search/2.0/
- Key IIIF Manifest example (has seeAlso ALTO links): https://iiif.wellcomecollection.org/presentation/b21211024

### Reference implementations to consult
These are **read-only references** — do not modify them.
- **Wellcome**: `C:/git/wellcomecollection/iiif-builder/src/Wellcome.Dds/`
  - Core models: `Wellcome.Dds/WordsAndPictures/` — `Text`, `Word`, `Image`, `ComposedBlock`, `ResultRect`
  - Builder: `Wellcome.Dds.Repositories/WordsAndPictures/AltoSearchTextProvider.cs`
  - Search controller: `Wellcome.Dds.Server/Controllers/SearchController.cs`
- **St Louis Fed**: `C:/git/digirati-co-uk/st-louis-fed/src/IIIFBuilder/`
  - Models: `IIIFBuilder.Models/WordsAndPictures/`
  - Builder: `IIIFBuilder.Processor/Alto/Building/TextBuilder.cs`, `AltoRescaler.cs`
  - Search: `IIIFBuilder.API/Features/Search/`

The goal is a recognisably similar implementation to these two, using the same proven `Text`/`Word`/`Image`/`ComposedBlock` model with Protobuf serialisation, then improving later if needed.

---

## Solution Structure

```
C:/git/tomcrane/TextServices/
├── instructions/           # Project brief and decision log (not code)
├── src/
│   ├── TextServices.sln
│   ├── TextServices.Core/          # Class library — models, text building, format providers
│   ├── TextServices.Storage/       # Storage abstraction + filesystem + S3 implementations
│   ├── TextServices.Infrastructure/ # Shared ASP.NET middleware — Serilog, CorrelationId
│   ├── TextServices.Builder.Api/   # ASP.NET 10 — async job API for building Text artefacts
│   ├── TextServices.Search.Api/    # ASP.NET 10 — IIIF Search and Autocomplete API
│   ├── TextServices.Tests/         # XUnit + FluentAssertions unit/integration tests
│   └── TextServices.Tests.E2E/     # Playwright end-to-end tests
└── CLAUDE.md
```

### TextServices.Core
The portable class library. No ASP.NET, no storage, no HTTP. Callers supply `XElement` instances per page.

Key classes (modelled closely on the reference implementations):
- `Text` — Protobuf-serialised root object: `NormalisedFullText`, `RawFullText`, `Words` (Dictionary<int,Word>), `Images` (Image[]), `ComposedBlocks` (ComposedBlock[])
- `Word` — Protobuf-serialised word with bounding box and position fields
- `Image` — Protobuf-serialised page boundary (`StartCharacter`, `ImageIdentifier`)
- `ComposedBlock` — Protobuf-serialised table/illustration region
- `ResultRect` — search hit bounding box (not persisted, computed at query time)
- `AutoComplete` — **separate Protobuf object** (`Buckets: Dictionary<string, HashSet<string>>`); stored independently from `Text`
- `TextBuildResult` — wraps `Text` + `AutoComplete` + `IsEmpty` flag
- `ITextFormatProvider` — interface for pluggable text-segmentation formats (ALTO, hOCR, etc.)
- `AltoTextFormatProvider` — METS-ALTO implementation; handles ns-v2 and ns-v3 namespaces, hyphenation (SUBS_TYPE="HypPart1"), composedblocks
- `TextBuilder` — accumulator that takes a sequence of `(id, width, height, XElement)` entries and returns a `TextBuildResult`

**Important design note**: The class library does NOT fetch URIs. The caller (Builder API) fetches ALTO files and passes `XElement` to the library.

### TextServices.Storage
Storage abstraction and implementations.

- `ITextStore` — interface: `SaveText(key, Text)`, `LoadText(key)`, `SaveAutoComplete(key, AutoComplete)`, `LoadAutoComplete(key)`, `SaveManifest(key, string json)`, `LoadManifest(key)`, `Exists(key)`
- `FileSystemTextStore` — stores files under a configured root path
- `S3TextStore` — stores objects in a configured S3 bucket

Keys are job IDs (e.g. `"2/books/my-book"`) which may contain `/` characters — implementations must handle this (e.g. as path segments or S3 key prefix).

### TextServices.Builder.Api
ASP.NET 10 minimal API or controller-based.

**Infrastructure:**
- PostgreSQL via EntityFramework Core + EF Migrations
- Hangfire (with `Hangfire.PostgreSql`) for durable background job processing
- `ITextStore` injected (filesystem for local dev, S3 for cloud)
- `HttpClient` (via `IHttpClientFactory`) for fetching Manifests and ALTO files

**Endpoints:**
- `POST /textbuilder` — accepts a job instruction (see data shapes below), enqueues a Hangfire job, returns HTTP 202 with `Location: /textbuilder/{id}`
- `GET /textbuilder/{**id}` — returns current job state (polling)
- `DELETE /textbuilder/{**id}` — cancels/removes a job (TBD)

**Job instruction (POST body):**
```json
{
  "id": "2/books/my-book",
  "sourceUri": "https://iiif.wellcomecollection.org/presentation/b21211024"
}
```
or with inline data:
```json
{
  "id": "2/books/my-book",
  "sourceData": [
    { "id": "page/1", "width": 4000, "height": 6000, "text": "https://..." },
    { "id": "page/2", "width": 4004, "height": 6006, "text": "https://..." }
  ]
}
```
Exactly one of `sourceUri` or `sourceData` must be present.

**Job state (GET response):**
```json
{
  "id": "2/books/my-book",
  "sourceUri": "https://...",
  "sourceData": null,
  "created": "2026-03-28T17:56:52Z",
  "started": null,
  "finished": null,
  "totalPages": 0,
  "pagesCompleted": 0,
  "totalWordCount": 0,
  "totalImageCount": 0,
  "errors": null,
  "searchV1": null,
  "autocompleteV1": null,
  "searchV2": null,
  "autocompleteV2": null
}
```
The `searchV1` etc. URLs are computed from configuration (base URL) + job ID, not stored in the DB.

**IIIF Manifest handling (when sourceUri points to a Manifest):**
- Fetch and parse as IIIF Presentation 3 JSON (not older versions — reject if not v3)
- Reduce to the `sourceData` page sequence: Canvas `id` → `id`, Canvas `width`/`height` → dimensions, `seeAlso` ALTO link → `text` URI
- Detect ALTO seeAlso links by `profile` containing `alto` (case-insensitive) or `label` containing "ALTO" or "METS-ALTO"
- Canvases without an ALTO seeAlso are included with `text: null` (sparse sequences are normal, not errors)
- Store a copy of the original Manifest JSON alongside the Text artefacts

### TextServices.Search.Api
ASP.NET 10, separate application (separate process/deployment from Builder API).

Uses MediatR for request/response pattern.

**Endpoints:**
- `GET /search/v1/{**id}?q={term}` — IIIF Search API v1 response
- `GET /autocomplete/v1/{**id}?q={term}` — IIIF Search API v1 autocomplete response
- `GET /text-augmented/v3/{**id}` — stored Manifest JSON decorated with search service links

**text-augmented behaviour:**
- Load stored Manifest JSON
- Set `id` to the fully-qualified URL of `/text-augmented/v3/{id}`
- Append search/autocomplete service descriptors to the `service` array (create if absent)
- Return as `application/json` (or `application/ld+json`)
- The application treats the Manifest as plain JSON (no full IIIF parser needed)

**Caching:**
- Memory-cache loaded `Text` and `AutoComplete` objects (sliding expiration)
- Use `AsyncKeyedLock` to prevent thundering-herd on cache misses

---

## Key Technical Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Root namespace | `TextServices` | Simple, unambiguous |
| Text serialisation | protobuf-net | Proven in production; compact binary; fast |
| AutoComplete storage | Separate file from Text | St Louis pattern — Search API can load only what it needs |
| Job queue | Hangfire + PostgreSQL | Durable, survives restarts; works locally, on AWS RDS, Azure PostgreSQL |
| IIIF JSON building | Plain `System.Text.Json` | No iiif-net dependency for now; may add later |
| Request/response pattern | MediatR | Testable; decouples HTTP from business logic |
| ORM | EntityFramework Core + Migrations | Standard; PostgreSQL via Npgsql |
| IIIF Presentation support | v3 only | Brief specifies v3 only for Builder API |
| Search API version | v1 first, v2 later | Brief specifies this phasing |
| Text format extensibility | `ITextFormatProvider` interface | Don't tie to ALTO; hOCR etc. to follow |
| ALTO namespace support | Both ns-v2 and ns-v3 | Multiple ALTO versions exist in the wild |
| Builder/Search separation | Two separate ASP.NET apps | No resource contention; builder can be turned off |

---

## Development Workflow

- Work in feature branches; submit well-described PRs to `main`
- The solution lives in `src/`; instructions in `instructions/`; this file at repo root
- Build up code incrementally — do not attempt to deliver the whole solution in one PR
- Tests should be written alongside the code they test, not after

### Suggested PR sequence
1. Solution scaffold + `TextServices.Core` models (Protobuf contracts)
2. `TextServices.Core` ALTO parsing + `TextBuilder`
3. `TextServices.Storage` abstraction + filesystem implementation
4. `TextServices.Builder.Api` — EF schema, job CRUD, Hangfire wiring
5. `TextServices.Builder.Api` — IIIF Manifest fetching + reduction to page sequence
6. `TextServices.Builder.Api` — full job processing pipeline
7. `TextServices.Search.Api` — search and autocomplete endpoints
8. `TextServices.Search.Api` — text-augmented Manifest endpoint
9. `TextServices.Storage` — S3 implementation
10. `TextServices.Tests.E2E` — integration test suite

---

## Testing

- **Unit/integration tests**: XUnit + FluentAssertions in `TextServices.Tests`
- **Integration tests**: `Microsoft.AspNetCore.Mvc.Testing` in `TextServices.Tests.E2E`
- **Test fixtures**: Random Wellcome Manifests from `https://iiif.wellcomecollection.org/service/suggest-b-number?q=imfeelinglucky`; use returned b-number in `https://iiif.wellcomecollection.org/presentation/{b-number}`
- Fixtures cover a good mix: small manifests, large (100s of canvases), manifests with no ALTO links
- Existing Wellcome search services can be used for comparative validation

---

## Key NuGet Packages

| Package | Project(s) | Purpose |
|---|---|---|
| `protobuf-net` | Core, Storage | Binary serialisation of Text models |
| `Hangfire.Core` + `Hangfire.AspNetCore` + `Hangfire.PostgreSql` | Builder.Api | Durable background job queue |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Builder.Api | PostgreSQL EF provider |
| `MediatR` | Builder.Api, Search.Api | Request/response decoupling |
| `AsyncKeyedLock` | Search.Api | Per-key async locking for cache |
| `Microsoft.Extensions.Caching.Memory` | Search.Api | In-process memory cache |
| `AWSSDK.S3` | Storage | S3 implementation |
| `Microsoft.AspNetCore.Mvc.Testing` | Tests.E2E | In-process HTTP integration tests |
| `FluentAssertions` | Tests | Readable test assertions |

---

## Important Implementation Notes

- **Coordinate rescaling**: ALTO records image dimensions at OCR time, which may differ from the IIIF Canvas dimensions. Always rescale word bounding boxes from ALTO dimensions to Canvas dimensions. See `AltoRescaler` in the St Louis reference.
- **Hyphenation**: Merge words with `SUBS_TYPE="HypPart1"` with the following word, omitting the hyphen from the normalised text.
- **Text normalisation**: Lowercase, collapse whitespace, strip non-alphanumeric characters. Consistent with both reference implementations.
- **Autocomplete buckets**: 3-character prefix bucketing. Only words longer than 2 characters. Results ordered by length then alphabetically.
- **Sparse manifests**: Canvases without ALTO are not errors — skip them silently and record the gap.
- **Job IDs with slashes**: Job IDs like `"2/books/my-book"` must be supported in URL routing (use catch-all route parameters `{**id}`).
- **Protobuf field numbers**: Once assigned, never change them. See reference implementations for the established numbering.
- **ComposedBlocks**: Capture table/illustration/figure bounding boxes from ALTO `<ComposedBlock>` elements (type="Table", "Illustration", "Figure"). See Wellcome reference.
- **IIIF Manifest as JSON**: The Search API treats stored Manifests as plain JSON objects — no need for a full IIIF object model. Use `JsonNode` or `JsonDocument` for patching.
- **Database credentials**: Ask the user for PostgreSQL admin credentials before running the first EF migration.
