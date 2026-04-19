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
| PDF | Yes | Requires image services in the Manifest |
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
| PDF | Yes (requires image services) |
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

The `/text-augmented/v3/{id}` endpoint returns the original Manifest JSON with the following
augmentations applied. Each augmentation has a condition — if the condition is not met (e.g.
no words were indexed, or no image services were found), that augmentation is silently omitted.

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
and the Manifest contains IIIF Image API services that the PDF renderer can use. Temporal-only
sources (audio/video with WebVTT) never produce a PDF.

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
  "label": { "en": ["Line-level transcription"] } }
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

## Documentation

- [Builder API reference](docs/builder-api.md)
- [Search API reference](docs/search-api.md)
