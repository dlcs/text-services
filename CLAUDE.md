# TextServices — Claude Code Guide

## Project Overview

A .NET 10 C# solution providing IIIF Text Services:

1. **TextServices.Core** — class library for building `Text` search index objects from ALTO, hOCR, VTT, and W3C Annotation files.
2. **TextServices.Builder.Api** — async job-based HTTP API that accepts IIIF Manifests or inline page sequences and produces stored artefacts.
3. **TextServices.Search.Api** — public-facing HTTP API providing IIIF Search v1/v2, autocomplete, annotations, plain text, PDF, figures, and text-augmented Manifest endpoints.
4. **TextServices.Pdf** — iText7-based PDF builder (used by Search API).
5. **TextServices.Demo** — lightweight static-file web app + config endpoint for local development and demos.

---

## Solution Structure

```
TextServices/
├── instructions/                    # Project brief and decision log (not code)
├── src/
│   ├── TextServices.sln
│   ├── TextServices.Core/           # Models, text building, format providers (no HTTP/storage)
│   ├── TextServices.Storage/        # ITextStore abstraction + filesystem + S3 implementations
│   ├── TextServices.Pdf/            # iText7 PDF builder
│   ├── TextServices.Infrastructure/ # Shared middleware — Serilog, CorrelationId
│   ├── TextServices.Builder.Api/    # ASP.NET 10 — job management + Hangfire pipeline
│   ├── TextServices.Search.Api/     # ASP.NET 10 — all read/search endpoints
│   ├── TextServices.Demo/           # ASP.NET 10 — static demo site + /demo-config
│   ├── TextServices.Tests/          # XUnit + FluentAssertions unit tests
│   └── TextServices.Tests.E2E/      # Playwright / WebApplicationFactory integration tests
└── CLAUDE.md
```

---

## TextServices.Core

No ASP.NET, no storage, no HTTP. Callers supply parsed content per page.

**Models** (Protobuf-serialised):
- `Text` — root object: `NormalisedFullText`, `RawFullText`, `Words` (`Dictionary<int,Word>`), `Images` (`Image[]`), `ComposedBlocks` (`ComposedBlock[]`)
- `Word` — word with bounding box and position
- `Image` — page boundary (`StartCharacter`, `ImageIdentifier`)
- `ComposedBlock` — table/illustration region
- `AutoComplete` — separate Protobuf object (`Buckets: Dictionary<string, HashSet<string>>`); stored independently from `Text`
- `ResultRect` — search hit bounding box (computed at query time, not persisted)
- `TextBuildResult` — wraps `Text` + `AutoComplete` + `IsEmpty`

**Providers** (implement `ITextFormatProvider`):
- `AltoTextFormatProvider` — METS-ALTO; handles ns-v2, ns-v3, namespace-free; hyphenation; ComposedBlocks
- `HocrTextFormatProvider` — hOCR HTML
- `VttTextFormatProvider` — WebVTT (implements `ITranscriptFormatProvider`)
- `W3cAnnotationTextFormatProvider` — W3C Web Annotation JSON (implements `IStringFormatProvider`)
- `AltoTextFormatProvider` is the default when profile/label are absent

**Entry point**: `TextBuilder` — accumulates `(id, width, height, source)` entries and returns a `TextBuildResult`.

---

## TextServices.Storage

**`ITextStore`** methods (key = job ID, may contain `/`):
- `SaveText` / `LoadText`
- `SaveAutoComplete` / `LoadAutoComplete`
- `SaveManifest` / `LoadManifest` — raw IIIF Manifest JSON
- `SaveRawText` / `LoadRawText` — un-normalised full text
- `SavePdf` / `LoadPdf` — searchable PDF derivative
- `SaveFigures` / `LoadFigures` — IIIF AnnotationPage JSON for ComposedBlocks
- `SaveAnnotations` / `LoadAnnotations` — manifest-level line annotations
- `SaveCapabilities` / `LoadCapabilities` — `JobServices` bitmask (null = all enabled)
- `SavePageSequence` / `LoadPageSequence` — ordered page list for sourceData jobs
- `DeleteArtefacts` — removes all artefacts for a key (call before reprocessing)
- `Exists` — checks whether a Text artefact exists

Implementations: `FileSystemTextStore`, `S3TextStore`.

---

## TextServices.Builder.Api

**Infrastructure**: PostgreSQL via EF Core + Migrations, Hangfire (PostgreSql), MediatR.

**Builder API endpoints:**
- `POST /textbuilder` — enqueue a job; returns 202 with `Location: /textbuilder/{id}`
- `GET /textbuilder` — list jobs (paged, filterable by status)
- `GET /textbuilder/{**id}` — job state
- `PUT /textbuilder/{**id}` — reprocess (re-enqueue) an existing job
- `DELETE /textbuilder/{**id}` — delete job record and stored artefacts

**Job instruction (POST/PUT body):**
```json
{
  "id": "2/books/my-book",
  "sourceUri": "https://iiif.wellcomecollection.org/presentation/b21211024"
}
```
or with inline pages:
```json
{
  "id": "2/books/my-book",
  "sourceData": [
    { "id": "page/1", "width": 4000, "height": 6000, "text": "https://..." }
  ]
}
```

**`BuilderJob` entity** (PostgreSQL, snake_case naming): `Id`, `SourceUri`, `SourceDataJson`, `Status`, `Created`, `Started`, `Finished`, `TotalPages`, `PagesCompleted`, `TotalWordCount`, `TotalImageCount`, `Errors`, `HangfireJobId`, `Services` (bitmask), `Title`, `CustomTypesJson`.

**`TextBuildJob`** (Hangfire): fetches manifest/resources concurrently (bounded by `MaxConcurrentAltoFetches`), feeds `TextBuilder`, persists all artefacts, records per-page warnings without aborting the job.

**Services that Builder.Api registers**: `IResourceFetcher`, `IManifestFetcher`, `IManifestReducer`, `IManifestSynthesiser`, `IAltoFetcher`, `IVttFetcher`, `IAnnotationPageFetcher`, `ITextStore`.

**`ManifestReducer`**: uses `iiif-net` to parse IIIF Presentation 3 JSON. Reduces to a page sequence (Canvas id, width, height, seeAlso text URI). Detects ALTO by `profile` or `label` containing "alto". Canvases without ALTO are silently included with `text: null`.

**`ManifestSynthesiser`**: builds a synthetic IIIF Manifest from a `sourceData` page sequence (used when no source Manifest URI is available).

---

## TextServices.Search.Api

MediatR for all request/response. `ITextCache` with `AsyncKeyedLock` prevents thundering-herd on cache misses.

**Endpoints:**
- `GET /search/v1/{**id}?q=` — IIIF Search API v1
- `GET /search/v2/{**id}?q=` — IIIF Search API v2
- `GET /autocomplete/v1/{**id}?q=` — IIIF Search API v1 autocomplete
- `GET /autocomplete/v2/{**id}?q=` — IIIF Search API v2 autocomplete
- `GET /text-augmented/v3/{**id}` — stored Manifest decorated with search/autocomplete service descriptors
- `GET /plain-text/{**id}` — raw full text
- `GET /pdf/{**id}` — streaming searchable PDF
- `GET /figures/{**id}` — IIIF AnnotationPage of ComposedBlock regions
- `GET /annotations/{**id}` — manifest-level line annotations
- `GET /annotations/word/{**id}` — word-level annotations
- `GET /annotations/line/{**id}` — line-level annotations
- `GET /proxy/image` — optional file-URI image proxy (local dev only)
- `GET /cache/clear/{**id}` — evict a key from the in-memory cache

CORS: `*` on all responses (IIIF requirement). Response compression: Brotli + Gzip for JSON/ld+json.

---

## TextServices.Pdf

`PdfBuilder` class using **iText7**. Builds a searchable PDF from a Text artefact + image URLs. Registered in Search API as a singleton; images are fetched via a named `HttpClient`.

---

## TextServices.Demo

Static-file web app (index.html, builder.html, viewer.html, compare.html, annotations.html) plus `GET /demo-config` which injects Builder/Search API URLs and local fixture paths into the browser so JavaScript never hard-codes them.

---

## Key Technical Decisions

| Decision | Choice |
|---|---|
| Text serialisation | protobuf-net |
| AutoComplete storage | Separate file from Text |
| Job queue | Hangfire + PostgreSQL |
| IIIF parsing | iiif-net (Builder.Api) |
| IIIF JSON manipulation | `JsonNode`/`JsonDocument` (Search.Api) |
| Request/response pattern | MediatR |
| ORM | EF Core + Npgsql + snake_case naming |
| PDF generation | iText7 |
| IIIF Presentation support | v3 only |
| Search API versions | v1 + v2 both implemented |

---

## Development Workflow

- Feature branches → PRs to `main`
- Solution in `src/`, instructions in `instructions/`, this file at repo root
- EF migrations in `TextServices.Builder.Api/Migrations/`; apply via `dotnet ef database update` or the auto-apply on startup in Development

### Running locally

Both APIs need `appsettings.Development.json` with:
```json
{
  "ConnectionStrings": { "BuilderDb": "Host=localhost;Database=textservices;..." },
  "TextServices": {
    "Storage": { "RootPath": "C:/some/local/path" },
    "SearchApiBaseUrl": "https://localhost:7001"
  }
}
```

The Demo app needs `appsettings.Development.json` with Builder and Search API base URLs configured under the `Demo` section.

### Testing

- **Unit tests**: `TextServices.Tests` — XUnit + FluentAssertions; covers Core parsing, Search handlers, Builder services, Infrastructure middleware
- **E2E / integration tests**: `TextServices.Tests.E2E` — `WebApplicationFactory` + Playwright; fixtures under `TextServices.Tests.E2E/Fixtures/`
- Test fixtures: Wellcome Collection manifests; `https://iiif.wellcomecollection.org/service/suggest-b-number?q=imfeelinglucky` for random b-numbers

---

## Key NuGet Packages

| Package | Project(s) | Purpose |
|---|---|---|
| `protobuf-net` | Core, Storage | Protobuf serialisation |
| `iiif-net` | Builder.Api | IIIF Presentation 3 parsing |
| `Hangfire.Core` + `Hangfire.AspNetCore` + `Hangfire.PostgreSql` | Builder.Api | Background job queue |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Builder.Api | PostgreSQL EF provider |
| `EFCore.NamingConventions` | Builder.Api | snake_case column naming |
| `MediatR` | Builder.Api, Search.Api | Request/response decoupling |
| `AsyncKeyedLock` | Search.Api | Per-key async locking for cache |
| `itext7` | Pdf | PDF generation |
| `AWSSDK.S3` | Storage | S3 implementation |
| `Serilog.AspNetCore` | Builder.Api, Search.Api | Structured logging |

---

## Important Implementation Notes

- **Coordinate rescaling**: ALTO image dimensions may differ from Canvas dimensions — always rescale word bounding boxes.
- **Hyphenation**: Merge words with `SUBS_TYPE="HypPart1"` with the following word, omitting the hyphen from normalised text.
- **Text normalisation**: Lowercase, collapse whitespace, strip non-alphanumeric.
- **Autocomplete buckets**: 3-character prefix. Words longer than 2 characters only. Ordered by length then alphabetically.
- **Job IDs with slashes**: Handled throughout with catch-all route parameters `{**id}`.
- **Protobuf field numbers**: Never change once assigned.
- **`JobServices` bitmask**: Controls which derivatives are built and which endpoints are active. A null capabilities file means all services are enabled.
- **`DeleteArtefacts`**: Call before reprocessing a job so stale derivatives from a previous run don't persist.
- **`AllowFileImageProxy`**: Only enable in trusted local-dev environments.
