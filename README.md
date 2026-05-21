# TextServices

IIIF Text Services — a .NET 10 solution that indexes the text content of IIIF Manifests and
provides IIIF Content Search, Autocomplete, and a suite of text-derived annotation endpoints.

## What it does

1. The **Builder API** accepts a IIIF Manifest (or an explicit list of pages) and builds a binary
   text index — a compact map of every word, its bounding box, and its position on every canvas.
   It also produces several stored derivatives from that index.
2. The **Search API** serves those artefacts through IIIF-standard endpoints and returns a
   decorated version of the original Manifest that is ready to load directly in a IIIF viewer.

## Architecture

```
 caller                  Builder API              Search API
  |                         |                           |
  |-- POST /textbuilder  -->|  fetches Manifest +       |
  |<-- 202 + Location ------|  text files; builds &     |
  |                         |  stores index artefacts   |
  |-- GET /textbuilder/id ->|                           |
  |<-- job status ----------|                           |
  |                         |                           |
  |-- GET /text-augmented/v3/id ----------------------->|
  |<-- decorated Manifest ------------------------------|
  |                                                     |
  |-- GET /search/v2/id?q=term ------------------------>|
  |<-- IIIF Search v2 AnnotationPage -------------------|
```

The two services share a storage backend (filesystem or S3). The Builder API writes artefacts
once; the Search API reads them. They can be deployed and scaled independently.

---

## Supported text formats

The Builder API selects a text-format provider automatically based on the `profile`, `format`,
and `label` metadata of each `seeAlso` or `annotations` link in the Manifest.

| Format | Detection |
|---|---|
| **METS-ALTO** (v2 / v3) | `seeAlso` profile contains `alto` (case-insensitive), or label contains `ALTO` / `METS-ALTO` |
| **hOCR** | `seeAlso` profile contains `hocr`, or label contains `hOCR` |
| **WebVTT** | format `text/vtt`, profile contains `vtt`, or label contains `vtt` / `webvtt` / `transcript` |
| **W3C Annotations** | External `AnnotationPage` with an `id` (fetched from the canvas `annotations` array) |

When neither `profile` nor `label` is present on a `seeAlso` entry, ALTO is assumed (the most
common case for unattributed XML).

---

## What gets built

What the Builder API can produce depends on the text format supplied.

### METS-ALTO

ALTO provides per-word bounding boxes, so every word is indexed with a spatial position on the
canvas. The builder also extracts `ComposedBlock` elements (tables, illustrations, figures) when
they are present.

Produced artefacts:

| Artefact | Always? | Condition |
|---|---|---|
| Text index (word positions + search) | Yes | — |
| AutoComplete index | Yes | — |
| Plain text | Yes | — |
| PDF | Yes | Requires at least one canvas with an image URL in the Manifest's painting annotations |
| Manifest-level line annotations | Yes | — |
| Figures / tables / illustrations | **Only if present in source** | ALTO `<ComposedBlock>` elements with non-zero dimensions |

### hOCR

hOCR also provides per-word bounding boxes. No `ComposedBlock` equivalent exists in hOCR, so
figures are never extracted.

| Artefact | Always? |
|---|---|
| Text index (word positions + search) | Yes |
| AutoComplete index | Yes |
| Plain text | Yes |
| PDF | Yes (requires image URLs in painting annotations) |
| Manifest-level line annotations | Yes |
| Figures / tables / illustrations | **Never** |

### WebVTT (audio / video captions)

WebVTT is time-coded, not spatially positioned. Words carry `#t=` time fragments instead of
`#xywh=` bounding boxes. Full-text search works, but spatial overlays and PDFs do not apply.

| Artefact | Always? |
|---|---|
| Text index (word positions + search) | Yes |
| AutoComplete index | Yes |
| Plain text | Yes |
| PDF | **Never** (no page images) |
| Manifest-level line annotations (temporal) | Yes |
| Figures / tables / illustrations | **Never** |

### W3C Annotations (`AnnotationPage`)

When a canvas has an external `AnnotationPage` link in its `annotations` array, the Builder API
fetches that page and extracts words from `TextualBody` items. Bounding boxes are taken from the
annotation `target` fragment (`#xywh=` or `#t=`).

This path is used for Manifests that already carry their own transcription annotations — for
example, a Manifest produced by a crowd-sourced transcription tool. The words from those
annotations are re-indexed so that IIIF Content Search and all the derived endpoints work against
them, even if the original source did not provide a search service.

| Artefact | Notes |
|---|---|
| Text index (word positions + search) | Yes |
| AutoComplete index | Yes |
| Plain text | Yes |
| PDF | Only if annotations have `#xywh=` targets and image services are present |
| Manifest-level line annotations | Yes |
| Figures / tables / illustrations | **Never** |

---

## Augmentations made to `/text-augmented/v3/{id}`

The `/text-augmented/v3/{id}` endpoint loads the Manifest stored by the Builder API and injects
search services, annotation links, and rendering links on-the-fly, then returns the result.

What is stored — and therefore what the caller receives — depends on the job source:

- **`sourceUri` jobs**: the original Manifest is fetched once, stored unmodified, and served as
  the base. The caller gets back the publisher's full Manifest — labels, thumbnails, metadata,
  rights, existing services — with the new augmentations added. Nothing in the original is
  changed or removed.
- **`sourceData` jobs**: no source Manifest exists. The Builder API synthesises a minimal skeleton
  at build time containing only one canvas per page (with canvas id, dimensions, and optional
  painting annotation). The caller gets this skeleton plus the augmentations. Labels, thumbnails,
  and other descriptive metadata are absent.

Each augmentation below has a condition — if the condition is not met (e.g. no words were indexed,
or no canvases have painting annotations), that augmentation is silently omitted.

### `service` array — search services

Added when: the text index contains at least one word.

```json
[
  {
    "id": "https://search.example.org/search/v2/my-collection/my-book",
    "type": "SearchService2",
    "service": [{ "id": "…/autocomplete/v2/…", "type": "AutoCompleteService2" }]
  },
  {
    "id": "https://search.example.org/search/v1/my-collection/my-book",
    "type": "SearchService1",
    "service": [{ "id": "…/autocomplete/v1/…", "type": "AutoCompleteService1" }]
  }
]
```

SearchService2 (IIIF Search 2) is listed first; SearchService1 (IIIF Search 1) follows for
backward-compatible viewers. Any pre-existing `service` entries in the Manifest are preserved.

### `rendering` array — plain text and PDF

**Plain text link** — added when: any words were indexed.

**PDF link** — added when: at least one image-based (non-temporal) canvas has words indexed
and the stored Manifest has painting annotations with image URLs the PDF renderer can fetch.
For `sourceUri` jobs these come from the source Manifest's own painting annotations; for
`sourceData` jobs they are the `imageUri` values echoed into the synthesised Manifest.
Temporal-only sources (audio/video with WebVTT) never produce a PDF.

### Per-canvas `annotations` — line and word annotation pages

Added to each canvas's `annotations` array when: that canvas has at least one word indexed.
Two references are added per canvas — one for line-level and one for word-level annotations.

```json
[
  { "id": "…/annotations/lines/v1/0/my-collection/my-book", "type": "AnnotationPage",
    "label": { "en": ["Line-level transcription"] } },
  { "id": "…/annotations/words/v1/0/my-collection/my-book", "type": "AnnotationPage",
    "label": { "en": ["Word-level transcription"] } }
]
```

These annotation pages are generated dynamically from the in-memory index on each request.
Canvas index `{n}` is zero-based and precedes the job ID in the URL (a routing constraint).

### Manifest-level `annotations` — full-document line annotations

Added to the manifest's top-level `annotations` array when: any words were indexed.

```json
{ "id": "…/annotations/manifest/v1/my-collection/my-book", "type": "AnnotationPage",
  "profile": "https://dlcs.io/profiles/all-text",
  "label": { "en": ["Text of all canvases"] } }
```

This is a single stored `AnnotationPage` covering every canvas, at line granularity. It is
built once at index time and served directly from storage — unlike the per-canvas pages, which
are generated dynamically. It is intended for bulk harvesting: callers can fetch this one file
to obtain all line annotations for the document without iterating individual canvas endpoints.

### Manifest-level `annotations` — figures, tables and illustrations

Added to the manifest's top-level `annotations` array when: the source contained METS-ALTO
`<ComposedBlock>` elements with non-zero dimensions.

```json
{ "id": "…/identified/figures/my-collection/my-book", "type": "AnnotationPage",
  "label": { "en": ["Figures, tables and illustrations"] } }
```

This is never produced for hOCR, WebVTT, or W3C Annotation sources — only METS-ALTO.

---

## Summary: augmentations by source format

| Augmentation | METS-ALTO | hOCR | WebVTT | W3C Annotations |
|---|:---:|:---:|:---:|:---:|
| Search + Autocomplete services | ✓ | ✓ | ✓ | ✓ |
| Plain text rendering link | ✓ | ✓ | ✓ | ✓ |
| PDF rendering link | ✓ | ✓ | — | ✓ (spatial only) |
| Per-canvas line + word annotations | ✓ | ✓ | ✓ (temporal) | ✓ |
| Manifest-level line annotations | ✓ | ✓ | ✓ (temporal) | ✓ |
| Figures / tables / illustrations | ✓ (if present) | — | — | — |

---

## Quick start

### 1 — Build a text index

```http
POST /textbuilder
Content-Type: application/json

{
  "id": "my-collection/my-book",
  "sourceUri": "https://example.org/iiif/my-book/manifest"
}
```

Add a `"services"` integer field to restrict which endpoints are built (omit for all). See
[Service flags](docs/builder-api.md#service-flags) for the flag values.

The response is `202 Accepted` with a `Location` header. Poll until `status` is `Completed`:

```http
GET /textbuilder/my-collection/my-book
```

### 2 — Search

```http
GET /search/v2/my-collection/my-book?q=annual+report
```

### 3 — Load the decorated Manifest in a viewer

```http
GET /text-augmented/v3/my-collection/my-book
```

Paste this URL into Universal Viewer, Clover, or any IIIF v3 viewer that supports IIIF Content
Search. The Manifest returned already has `SearchService2` and `SearchService1` injected.

---

## Local development setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL 14 or later (used by the Builder API for job state and the Hangfire queue)

### Ports (Development profile)

| Application | URL |
|---|---|
| Builder API | http://localhost:5283 |
| Search API | http://localhost:5294 |
| Demo UI | http://localhost:5100 |

The Builder API and Search API share a storage directory (`C:/textservices-data` by default on
Windows). Both must point at the same path.

### 1 — Configure the Builder API

Create `src/TextServices.Builder.Api/appsettings.Development.json` (not committed — add your own
credentials):

```json
{
  "ConnectionStrings": {
    "BuilderDb": "Host=localhost;Database=textservices_builder;Username=postgres;Password=your-password"
  },
  "TextServices": {
    "SearchApiBaseUrl": "http://localhost:5294",
    "AllowFileImageProxy": true
  }
}
```

`AllowFileImageProxy: true` lets the Demo UI serve locally stored images through the Search API's
`/proxy/image` endpoint — only enable this in local development.

### 2 — Run the EF Core migrations

The Builder API manages its own database schema. Run the migrations once (and again after any
future schema changes):

```bash
cd src/TextServices.Builder.Api
dotnet ef database update
```

If `dotnet ef` is not installed: `dotnet tool install -g dotnet-ef`

### 3 — Configure the Search API (optional)

The Search API has no database and works out of the box for local development. If you need to
override defaults, create `src/TextServices.Search.Api/appsettings.Development.json`:

```json
{
  "TextServices": {
    "BaseUrl": "http://localhost:5294",
    "AllowFileImageProxy": true
  }
}
```

### 4 — Start the applications

Open three terminals and run each application:

```bash
# Terminal 1 — Builder API
cd src/TextServices.Builder.Api
dotnet run
```

```bash
# Terminal 2 — Search API
cd src/TextServices.Search.Api
dotnet run
```

```bash
# Terminal 3 — Demo UI
cd src/TextServices.Demo
dotnet run
```

Then open http://localhost:5100 in a browser.

### 5 — Run the tests

```bash
cd src
dotnet test TextServices.Tests/TextServices.Tests.csproj
```

The unit/integration tests run entirely in-process and do not require a running database or
either API to be up.

### 6 — Code formatting

This project enforces `dotnet format` on every commit via [`pre-commit`](https://pre-commit.com/).
Install it once (requires Python):

```bash
pip install pre-commit   # or: brew install pre-commit / scoop install pre-commit
```

Then wire up the hook once per clone:

```bash
pre-commit install
```

After that, `dotnet format` runs automatically on `git commit` and will block the commit if any
C# files need reformatting. To run it manually across all files:

```bash
pre-commit run --all-files
```

To auto-fix formatting issues without committing:

```bash
dotnet format src/TextServices.sln
```

### Storage directory

The default storage root is `C:/textservices-data`. The Builder API creates subdirectories
automatically as jobs complete. On Linux/macOS, change `Storage:RootPath` in
`appsettings.Development.json` to a writable path (e.g. `/tmp/textservices-data`).

The Search API must be configured with the same path via `StorageRootPath`.

---

## Docker

Three Dockerfiles are provided at the repo root — one per application — all using a two-stage
build (SDK for compile, ASP.NET runtime for the final image). The build context is always the
**repo root**.

### docker-compose (recommended for local dev)

`docker-compose.yml` brings up the full stack with a single command:

```bash
docker compose up --build
```

| Service | Host port | Description |
|---|---|---|
| `postgres` | 5452 | PostgreSQL 14 (job state + Hangfire queue) |
| `builder` | 5283 | Builder API |
| `search` | 5294 | Search API |
| `demo` | 5100 | Demo UI |

The Builder and Search APIs share a named Docker volume (`txt_textservices_data`) for text
artefacts. The Builder API applies EF Core migrations automatically on startup
(`RunMigrations=true`).

Both APIs run with `ASPNETCORE_ENVIRONMENT=Development`, which enables the OpenAPI docs
(`/openapi/v1.json`) and the Hangfire dashboard (`http://localhost:5283/hangfire`).

After startup, open http://localhost:5100.

### Building images individually

```bash
docker build -f Dockerfile.Builder -t textservices-builder .
docker build -f Dockerfile.Search  -t textservices-search .
docker build -f Dockerfile.Demo    -t textservices-demo .
```

Each image exposes port `8080`. Configuration is supplied via environment variables using the
standard .NET double-underscore separator for nested keys (e.g.
`TextServices__Storage__RootPath=/data`).

---

## Documentation

- [Builder API reference](docs/builder-api.md)
- [Search API reference](docs/search-api.md)
