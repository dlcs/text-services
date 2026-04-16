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
| `sourceUri` | string | one of | URI of a IIIF Presentation 3 Manifest. The Builder API fetches and reduces it automatically. |
| `sourceData` | array | one of | Inline page sequence (see [sourceData format](#sourcedata-format)). |

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
  "searchV1": null,
  "autocompleteV1": null,
  "searchV2": null,
  "autocompleteV2": null
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
| `searchV1` | string\|null | URL of the IIIF Search v1 endpoint (populated on completion). |
| `autocompleteV1` | string\|null | URL of the IIIF Autocomplete v1 endpoint (populated on completion). |
| `searchV2` | string\|null | URL of the IIIF Search v2 endpoint (populated on completion). |
| `autocompleteV2` | string\|null | URL of the IIIF Autocomplete v2 endpoint (populated on completion). |

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
      "text": "https://example.org/alto/page-1.xml",
      "profile": "http://www.loc.gov/standards/alto/v3/alto.xsd",
      "format": null,
      "label": null
    },
    {
      "id": "https://example.org/canvas/2",
      "width": 3000,
      "height": 4000,
      "text": null
    }
  ]
}
```

| Field | Description |
|---|---|
| `id` | Canvas identifier URI. Used as the canvas reference in search results. |
| `width` | Canvas width in pixels. Used to rescale ALTO coordinates when ALTO and canvas dimensions differ. |
| `height` | Canvas height in pixels. |
| `text` | URI of the text file for this canvas. `null` for canvases without text (sparse pages are normal). |
| `profile` | IIIF `seeAlso` profile URI — used alongside `format` to select the correct text-format provider. |
| `format` | MIME type (e.g. `text/vtt`). |
| `label` | Fallback display label; used for provider selection when `profile` is absent. |

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

Canvases that match none of these are included as sparse pages (`text: null`) and contribute no
words to the index.

### Recognised text formats

| Format | Detection |
|---|---|
| METS-ALTO v2 / v3 | `seeAlso` profile contains `alto` or label contains `ALTO` / `METS-ALTO` |
| hOCR | `seeAlso` profile contains `hocr` or label contains `hOCR` |
| WebVTT | `seeAlso` / `annotations` format `text/vtt`, profile contains `vtt`, or label contains `vtt` / `webvtt` / `transcript` |
| W3C Annotations | External `AnnotationPage` (detected automatically as described above) |

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
| `SearchApiBaseUrl` | `""` | Public base URL of the Search API. Used to populate the `searchV1` / `searchV2` fields in completed job responses. Leave empty if the Search API is not yet deployed. |
| `MaxConcurrentAltoFetches` | `8` | Maximum number of text files fetched in parallel within a single job. Increase for internal or S3 sources; keep low (4–8) for third-party HTTP hosts. |
| `Storage:RootPath` | `textservices-data` | Root directory for stored text artefacts. Must be readable by the Search API. |
| `CorsAllowedOrigins` | `[]` | Allowed CORS origins. Empty array disables CORS. |
