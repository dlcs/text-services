# TextServices

IIIF Text Services — a .NET 10 solution that indexes the text content of IIIF Manifests and
provides IIIF Content Search and Autocomplete API endpoints backed by that index.

## What it does

1. The **Builder API** accepts a IIIF Manifest (or an explicit list of pages) and builds a binary
   text index — a compact in-memory map of every word and its position on every canvas.
2. The **Search API** serves that index through standard IIIF Search v1 and v2 endpoints, plus a
   decorated version of the original Manifest that is ready to load directly in a IIIF viewer.

Supported text formats: METS-ALTO (v2 and v3), hOCR, WebVTT, W3C Web Annotation
(`AnnotationPage` with `TextualBody` items), and inline `sourceData` supplied by the caller.

## Architecture

```
 caller                Builder API                  Search API
  │                        │                             │
  │── POST /textbuilder ──►│  fetches Manifest + text    │
  │◄── 202 + Location ─────│  files; builds & stores     │
  │                        │  index artefacts            │
  │── GET  /textbuilder/id ►│                             │
  │◄── job status ─────────│                             │
  │                        │                             │
  │                                                       │
  │── GET /text-augmented/v3/id ─────────────────────────►│
  │◄── decorated Manifest ───────────────────────────────│
  │                                                       │
  │── GET /search/v2/id?q=term ──────────────────────────►│
  │◄── IIIF Search v2 AnnotationPage ────────────────────│
```

The two services share a storage backend (filesystem or S3). The Builder API writes; the Search
API reads. They can be deployed and scaled independently.

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

The Builder API fetches the Manifest, locates text files linked via `seeAlso` or `annotations`,
and builds the index. The response is `202 Accepted` with a `Location` header.

Poll the location until `status` is `Completed`:

```http
GET /textbuilder/my-collection/my-book
```

When complete, `searchV1`, `searchV2`, `autocompleteV1`, and `autocompleteV2` fields in the
response body contain the URLs of the live services.

### 2 — Search

```http
GET /search/v2/my-collection/my-book?q=annual+report
```

### 3 — Load the decorated Manifest in a viewer

```http
GET /text-augmented/v3/my-collection/my-book
```

This returns the original Manifest with `SearchService2` (and `SearchService1`) descriptors
injected — paste the URL into Universal Viewer, Clover, or any IIIF v3 viewer that supports
content search.

## Documentation

- [Builder API reference](docs/builder-api.md)
- [Search API reference](docs/search-api.md)
