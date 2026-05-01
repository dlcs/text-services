# Builder API

The Builder API accepts IIIF Manifests (or explicit page sequences), fetches their text files,
builds a binary text index, and stores the artefacts so the Search API can serve them.

Jobs are processed asynchronously via a Hangfire queue backed by PostgreSQL. Submit a job and
poll for completion; the text index is available as soon as `status` reaches `Completed`.

## Base path

All paths are relative to the Builder API base URL, e.g. `https://builder.example.org`.

---

## Endpoints

### POST /textbuilder — create a job

Enqueues a new text-building job.

**Request body**

```json
{
  "id": "my-collection/my-book",
  "sourceUri": "https://example.org/iiif/my-book/manifest"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Storage key and URL segment for all derived services. May contain `/`. |
| `sourceUri` | string | one of | URI of a IIIF Presentation 3 Manifest. The Builder API fetches the Manifest, stores an unmodified copy, then reduces it to extract the page sequence for text building. The stored copy is what the `/text-augmented/v3/{id}` endpoint serves, with search services and annotation links injected on-the-fly. All original canvas metadata, labels, thumbnails, and services are preserved. |
| `sourceData` | array | one of | Inline page sequence (see [sourceData format](#sourcedata-format)). No source Manifest exists; the Builder API synthesises a minimal skeleton Manifest from the page list. |
| `services` | integer | no | Bitmask of services to build and expose. Default: `-1` (all enabled). See [Service flags](#service-flags). |

Exactly one of `sourceUri` or `sourceData` must be provided.

**Responses**

| Status | Meaning |
|---|---|
| `202 Accepted` | Job enqueued. `Location` header and body contain the job URL. |
| `409 Conflict` | A job with this `id` already exists. Retrieve it or reprocess it. |
| `400 Bad Request` | Validation error (missing `id`, both or neither source fields supplied). |

**Example response body (202)**

```json
{
  "id": "my-collection/my-book",
  "sourceUri": "https://example.org/iiif/my-book/manifest",
  "sourceData": null,
  "status": "Waiting",
  "created": "2026-04-16T09:00:00Z",
  "started": null,
  "finished": null,
  "totalPages": 0,
  "pagesCompleted": 0,
  "totalWordCount": 0,
  "totalImageCount": 0,
  "errors": null,
  "services": -1,
  "searchV1": null,
  "autocompleteV1": null,
  "searchV2": null,
  "autocompleteV2": null,
  "fullText": null,
  "pdf": null,
  "textAugmented": null,
  "annotations": null,
  "figures": null
}
```

---

### GET /textbuilder — list jobs

Returns a paged list of all jobs, newest first.

**Query parameters**

| Parameter | Default | Description |
|---|---|---|
| `page` | `1` | Page number (1-based). |
| `pageSize` | `20` | Items per page. |
| `status` | _(all)_ | Filter by status: `Waiting`, `Processing`, `Completed`, `Failed`. |

**Response** — a paged result wrapper:

```json
{
  "page": 1,
  "pageSize": 20,
  "total": 42,
  "items": [ ... ]
}
```

Each item has the same shape as the [job response](#job-response-fields).

---

### GET /textbuilder/{**id} — get job status

Returns the current state of a job.

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | Job found; body is the [job response](#job-response-fields). |
| `404 Not Found` | No job with this `id`. |

---

### PUT /textbuilder/{**id} — reprocess a job

Re-enqueues a previously completed or failed job. The existing text artefacts are overwritten
when the new run completes.

**Responses**

| Status | Meaning |
|---|---|
| `202 Accepted` | Reprocess job enqueued. |
| `404 Not Found` | No job with this `id`. |
| `409 Conflict` | The job is currently in `Waiting` or `Processing` state; it cannot be re-queued until it finishes. |

---

### DELETE /textbuilder/{**id} — delete a job

Removes the job record from the database. Does **not** delete the stored text artefacts.

**Responses**

| Status | Meaning |
|---|---|
| `204 No Content` | Job deleted. |
| `404 Not Found` | No job with this `id`. |

---

## Job response fields

| Field | Type | Description |
|---|---|---|
| `id` | string | Job identifier as supplied on creation. |
| `sourceUri` | string\|null | Manifest URI, if one was provided. |
| `sourceData` | array\|null | Inline page sequence, if one was provided. |
| `status` | string | `Waiting`, `Processing`, `Completed`, or `Failed`. |
| `created` | ISO 8601 | When the job was accepted. |
| `started` | ISO 8601\|null | When the Hangfire worker picked it up. |
| `finished` | ISO 8601\|null | When processing ended (success or failure). |
| `totalPages` | int | Total number of canvases in the source. |
| `pagesCompleted` | int | Canvases processed so far (useful for polling). |
| `totalWordCount` | int | Words indexed (populated on completion). |
| `totalImageCount` | int | Figures / tables / illustrations identified (populated on completion). |
| `errors` | string\|null | Error message if `status` is `Failed`. |
| `services` | int | Bitmask of enabled services (see [Service flags](#service-flags)). `-1` means all enabled. |
| `searchV1` | string\|null | URL of the IIIF Search v1 endpoint (populated on completion, when `Search` flag is set). |
| `autocompleteV1` | string\|null | URL of the IIIF Autocomplete v1 endpoint (populated on completion, when `Autocomplete` flag is set). |
| `searchV2` | string\|null | URL of the IIIF Search v2 endpoint (populated on completion, when `Search` flag is set). |
| `autocompleteV2` | string\|null | URL of the IIIF Autocomplete v2 endpoint (populated on completion, when `Autocomplete` flag is set). |
| `fullText` | string\|null | URL of the plain-text endpoint (populated on completion, when `FullText` flag is set). |
| `pdf` | string\|null | URL of the PDF endpoint (populated on completion, when `Pdf` flag is set). |
| `textAugmented` | string\|null | URL of the text-augmented Manifest endpoint (populated on completion, when `TextAugmented` flag is set). |
| `annotations` | string\|null | URL of the manifest-level line annotations endpoint (populated on completion, when `Annotations` flag is set). |
| `figures` | string\|null | URL of the figures annotation page (populated on completion, when `Figures` flag is set). |

---

## sourceData format

Use `sourceData` when you already know the page sequence and text file URLs and do not want the
Builder API to fetch and reduce a Manifest.

```json
{
  "id": "my-collection/my-book",
  "sourceData": [
    {
      "id": "https://example.org/canvas/1",
      "width": 3000,
      "height": 4000,
      "textUri": "https://example.org/alto/page-1.xml",
      "imageUri": "https://example.org/iiif/image/page-1",
      "profile": "http://www.loc.gov/standards/alto/v3/alto.xsd",
      "format": null,
      "label": null
    },
    {
      "id": "https://example.org/canvas/2",
      "width": 3000,
      "height": 4000,
      "textUri": null
    }
  ]
}
```

| Field | Description |
|---|---|
| `id` | Canvas identifier URI. Used as the canvas reference in search results. |
| `width` | Canvas width in pixels. Used to rescale ALTO coordinates when ALTO and canvas dimensions differ. |
| `height` | Canvas height in pixels. |
| `textUri` | URI of the text file for this canvas. `null` for canvases without text (sparse pages are normal). |
| `imageUri` | URL of the image for this canvas. Used as the `body.id` of the painting annotation in the synthesised Manifest, and as the image source when generating PDFs. For `sourceUri` jobs this value comes from the `body.id` of the source Manifest's own painting annotation — it is an already-resolved image URL and is not assumed to be a IIIF Image API service root. `http`/`https` URLs are used as-is; `file://` URIs are proxied via `GET /proxy/image` on the Search API (requires `SearchApiBaseUrl` — see [Configuration](#configuration)); `s3://` URIs return a placeholder image. Omit for temporal (audio/video) canvases. |
| `duration` | Canvas duration in seconds (double). Required for temporal (audio/video) canvases; omit for image-based pages. |
| `profile` | IIIF `seeAlso` profile URI — used alongside `format` to select the correct text-format provider. |
| `format` | MIME type (e.g. `text/vtt`). |
| `label` | Fallback display label; used for provider selection when `profile` is absent. |

### Stored Manifest — the two paths

The stored Manifest is the foundation for the `/text-augmented/v3/{id}`, `/pdf/v1/{id}`, and
annotation endpoints. How it is produced depends on which source was supplied:

**`sourceUri`** — the original Manifest is fetched once, stored unmodified, and never changed
again. When the text-augmented endpoint is called it loads this stored copy and injects search
services, annotation links, and rendering links on-the-fly, then returns the result. The Manifest
returned to the caller contains everything the publisher put in it — labels, thumbnails, metadata,
rights statements, existing services — plus the new augmentations.

**`sourceData`** — there is no source Manifest. The Builder API synthesises a minimal skeleton
containing only the content derivable from the page sequence: one canvas per page instruction,
with the canvas `id`, `width`, `height`, `duration`, and (if supplied) a painting annotation
whose `body.id` is the `imageUri`. Nothing else. The skeleton has no labels, no thumbnails, no
metadata, and no rights information. The text-augmented endpoint serves this skeleton with the
same on-the-fly augmentations applied.

Both paths store a JSON file. The difference is entirely in what that file contains.

### Synthetic Manifest for sourceData jobs

When a job is submitted with `sourceData`, the Builder API synthesises a skeleton IIIF
Presentation 3 Manifest from the page sequence and stores it alongside the text artefacts. This
means the text-augmented, PDF, and annotation endpoints work for `sourceData` jobs in exactly the
same way as for `sourceUri` jobs. The synthesised Manifest has an empty `id` (patched to the
`/text-augmented/v3/{id}` URL at serve time) and one canvas per page instruction.

**Painting annotations and `imageUri`**: When a page includes an `imageUri`, the synthesised canvas
carries a painting annotation with that URI as the `body.id`. The canvas dimensions (`width` /
`height`) reflect the canvas size as supplied — they are not the pixel dimensions of the image
itself, which are unknown. The annotation body is therefore emitted without `width` or `height`
properties.

URI scheme handling for `imageUri` in synthesised Manifests:

| Scheme | `AllowFileImageProxy: false` (default) | `AllowFileImageProxy: true` |
|---|---|---|
| `http` / `https` | Echoed back unchanged | Echoed back unchanged |
| `file://` | **Painting annotation omitted** — no file path in manifest | Proxy URL: `{SearchApiBaseUrl}/proxy/image?uri=…` |
| `s3://` | **Painting annotation omitted** | Proxy URL (returns placeholder PNG) |

`AllowFileImageProxy` defaults to `false` to prevent `file://` paths from being embedded in stored
Manifests and potentially exposing access-controlled images. Only enable it in trusted environments
such as local development.

---

---

## Text source detection from a Manifest

When `sourceUri` points to a IIIF Presentation 3 Manifest, the Builder API reduces each canvas
to a `PageInstruction` using the following priority order:

1. **`seeAlso`** — a linked resource with a profile or label that identifies it as ALTO, hOCR,
   or another supported format.
2. **Embedded `annotations`** — a canvas-level `AnnotationPage` that contains `items` (inline
   supplementing annotations with `TextualBody` values).
3. **External `AnnotationPage`** — a canvas-level `annotations` entry that has an `id` but no
   `items`, meaning the page must be fetched separately.

Canvases that match none of these are included as sparse pages (`textUri: null`) and contribute no
words to the index.

### Recognised text formats

| Format | Detection |
|---|---|
| METS-ALTO v2 / v3 | `seeAlso` profile contains `alto` or label contains `ALTO` / `METS-ALTO` |
| hOCR | `seeAlso` profile contains `hocr` or label contains `hOCR` |
| WebVTT | `seeAlso` / `annotations` format `text/vtt`, profile contains `vtt`, or label contains `vtt` / `webvtt` / `transcript` |
| W3C Annotations | External `AnnotationPage` (detected automatically as described above) |

---

## Service flags

The `services` field on a job request is an integer bitmask that controls which derivatives the
Builder API builds and which Search API endpoints respond. Submit the bitwise OR of the flags you
want enabled. Omitting `services` (or supplying `-1`) enables everything.

| Flag name | Value | What it controls |
|---|---|---|
| `Search` | `1` | IIIF Content Search v1 and v2 endpoints |
| `Autocomplete` | `2` | Autocomplete v1 and v2 endpoints |
| `FullText` | `4` | Plain-text endpoint (`/text/v1/`) |
| `Pdf` | `8` | PDF endpoint (`/pdf/v1/`) |
| `TextAugmented` | `16` | Text-augmented Manifest endpoint (`/text-augmented/v3/`) |
| `Annotations` | `32` | Line/word annotation pages and manifest-level annotations endpoint |
| `Figures` | `64` | Identified figures endpoint (`/identified/figures/`) |

**Example** — Search and Autocomplete only (`1 | 2 = 3`):

```json
{
  "id": "my-collection/my-book",
  "sourceUri": "https://example.org/iiif/my-book/manifest",
  "services": 3
}
```

When `services` is anything other than `-1`, the Builder API writes a `capabilities.json` file to
storage. The Search API reads this file and returns `404 Not Found` for any endpoint whose flag is
absent. Jobs that have no `capabilities.json` (pre-existing jobs or jobs submitted with the default)
treat all services as enabled — backward compatible by design.

---

## Configuration

Builder API configuration lives under the `TextServices` key in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "BuilderDb": "Host=localhost;Database=textservices;Username=...;Password=..."
  },
  "TextServices": {
    "SearchApiBaseUrl": "https://search.example.org",
    "MaxConcurrentAltoFetches": 8,
    "Storage": {
      "RootPath": "/data/textservices"
    }
  },
  "CorsAllowedOrigins": ["https://viewer.example.org"]
}
```

| Setting | Default | Description |
|---|---|---|
| `ConnectionStrings:BuilderDb` | _(required)_ | PostgreSQL connection string. Used for both EF Core (job state) and Hangfire (job queue). |
| `SearchApiBaseUrl` | `""` | Public base URL of the Search API. Used to populate the `searchV1` / `searchV2` fields in completed job responses, and to construct `/proxy/image` URLs in synthesised Manifests when `sourceData` pages supply `file://` or `s3://` imageUri values. Leave empty if the Search API is not yet deployed. |
| `MaxConcurrentAltoFetches` | `8` | Maximum number of text files fetched in parallel within a single job. Increase for internal or S3 sources; keep low (4–8) for third-party HTTP hosts. |
| `Storage:RootPath` | `textservices-data` | Root directory for stored text artefacts. Must be readable by the Search API. |
| `CorsAllowedOrigins` | `[]` | Allowed CORS origins. Empty array disables CORS. |
