# Search API

The Search API provides IIIF Content Search (v1 and v2) and Autocomplete endpoints, plus
several derived outputs: plain text, PDF, identified figures, and a search-service-decorated
copy of the original Manifest. All endpoints are read-only.

## Base path

All paths are relative to the Search API base URL, e.g. `https://search.example.org`.

All `{**id}` path segments are catch-all — they accept slash characters, matching the job IDs
created by the Builder API (e.g. `my-collection/my-book`).

---

## IIIF Search

### GET /search/v2/{**id}?q={term}

IIIF Content Search API v2 response. Clients that understand Presentation 3 should prefer this
endpoint.

**Query parameters**

| Parameter | Description |
|---|---|
| `q` | Search term (required for non-empty results). Omit or leave empty to return an empty `AnnotationPage`. |

**Response** — an `AnnotationPage` at `@context http://iiif.io/api/search/2/context.json`:

```json
{
  "@context": "http://iiif.io/api/search/2/context.json",
  "id": "https://search.example.org/search/v2/my-collection/my-book?q=annual+report",
  "type": "AnnotationPage",
  "items": [
    {
      "id": "https://.../anno/h0i0-490,502,2376,100",
      "type": "Annotation",
      "motivation": "painting",
      "body": {
        "type": "TextualBody",
        "value": "REPORT",
        "format": "text/plain"
      },
      "target": "https://example.org/canvas/1#xywh=490,502,2376,100"
    }
  ],
  "annotations": [
    {
      "type": "AnnotationPage",
      "items": [
        {
          "id": "https://.../anno/h0i0-490,502,2376,100",
          "type": "Annotation",
          "motivation": "contextualizing",
          "target": {
            "type": "SpecificResource",
            "source": "https://.../anno/h0i0-490,502,2376,100",
            "selector": [
              {
                "type": "TextQuoteSelector",
                "exact": "REPORT",
                "prefix": "ANNUAL ",
                "suffix": " OF THE"
              }
            ]
          }
        }
      ]
    }
  ]
}
```

Each item in `items` identifies a canvas region containing a matching word. Multi-word phrases
that span more than one canvas region produce multiple items. The `annotations` array provides
contextualising annotations (before/after context) for each hit.

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | Results (may be empty if no matches or `q` is absent). |
| `404 Not Found` | No text index exists for this `id`. |

---

### GET /search/v1/{**id}?q={term}

IIIF Content Search API v1 response. Use this for backward compatibility with viewers that do
not yet support v2.

**Response** — an `sc:AnnotationList`:

```json
{
  "@context": "http://iiif.io/api/search/1/context.json",
  "@id": "https://search.example.org/search/v1/my-collection/my-book?q=annual+report",
  "@type": "sc:AnnotationList",
  "within": { "@type": "sc:Layer", "total": 3 },
  "resources": [
    {
      "@id": "https://.../anno/h0i0-490,502,2376,100",
      "@type": "oa:Annotation",
      "motivation": "sc:painting",
      "resource": { "@type": "cnt:ContentAsText", "chars": "REPORT" },
      "on": "https://example.org/canvas/1#xywh=490,502,2376,100"
    }
  ],
  "hits": [
    {
      "@type": "search:Hit",
      "annotations": ["https://.../anno/h0i0-490,502,2376,100"],
      "match": "REPORT",
      "before": "ANNUAL ",
      "after": " OF THE"
    }
  ]
}
```

The `ignored` field is included when the query contained recognised-but-unsupported parameters
(`motivation`, `date`, `user`, `box`).

**Responses** — same as v2 above.

---

## Autocomplete

### GET /autocomplete/v2/{**id}?q={term}

IIIF Content Search API v2 autocomplete. Returns a `TermPage`.

**Query parameters**

| Parameter | Description |
|---|---|
| `q` | Partial term to complete. Queries shorter than 3 characters return an empty `TermPage`. |

**Response**

```json
{
  "@context": "http://iiif.io/api/search/2/context.json",
  "id": "https://search.example.org/autocomplete/v2/my-collection/my-book?q=ann",
  "type": "TermPage",
  "items": [
    { "value": "annual" },
    { "value": "annexed" }
  ]
}
```

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | Term list (may be empty). |
| `404 Not Found` | No text index for this `id`. |

---

### GET /autocomplete/v1/{**id}?q={term}

IIIF Content Search API v1 autocomplete. Returns a `search:TermList`.

**Response**

```json
{
  "@context": "http://iiif.io/api/search/1/context.json",
  "@id": "https://search.example.org/autocomplete/v1/my-collection/my-book?q=ann",
  "@type": "search:TermList",
  "terms": [
    { "@type": "search:Term", "match": "annual" },
    { "@type": "search:Term", "match": "annexed" }
  ]
}
```

---

## Text granularity annotations

These endpoints return IIIF Presentation 3 `AnnotationPage` objects generated on the fly from
the in-memory text index. Each annotation has `motivation: "supplementing"` and a `TextualBody`
carrying the raw word or line text. The response includes both the standard IIIF Presentation 3
context and the [IIIF Text Granularity extension](https://iiif.io/api/extension/text-granularity/)
context, with a `textGranularity` property indicating the level.

These endpoints are primarily intended for harvesting: callers can fetch all annotation pages
for a document, rewrite their URLs, and serve them independently. References to these pages are
injected into each canvas's `annotations` array by the `/text-augmented/v3/` endpoint.

**Cross-line hyphenation note:** When ALTO source material contains hyphenated words split across
two lines (using `SUBS_TYPE="HypPart1"` / `"HypPart2"`), the two fragments are merged into a
single word in the text index and placed on the **first** line (the line where the hyphen visually
appears). Consequences for line-level annotations:

- The first line's annotation body contains the merged form (e.g. `"schwarzweiß"`), not the
  raw hyphenated fragment (`"schwarz-"`).
- The second line's annotation starts at the word *after* the HypPart2 fragment — the
  continuation fragment does not appear again as the first word of that line.

This matches the behaviour of both reference implementations. It is a known trade-off of
single-occurrence word indexing: phrase search and bounding box accuracy are preserved at the
cost of a minor difference from the raw source text at hyphenated line breaks.

### GET /annotations/lines/v1/{n}/{**id}

Returns line-level annotations for canvas `{n}` (zero-based index into the manifest canvas
list) of job `{**id}`. Each annotation covers one text line; its target is the outer bounding
box of all words on that line. For temporal canvases the target uses an `#t=` fragment
(seconds, up to 3 decimal places) instead of `#xywh=`.

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | AnnotationPage (may have empty `items` for a sparse canvas). |
| `404 Not Found` | No text index for this `id`, or `{n}` is out of range. |

**Example response**

```json
{
  "@context": [
    "http://iiif.io/api/presentation/3/context.json",
    "https://iiif.io/api/extension/text-granularity/context.json"
  ],
  "id": "https://search.example.org/annotations/lines/v1/0/my-collection/my-book",
  "type": "AnnotationPage",
  "textGranularity": "line",
  "items": [
    {
      "id": "https://search.example.org/annotations/lines/v1/0/my-collection/my-book/anno/0",
      "type": "Annotation",
      "motivation": "supplementing",
      "body": { "type": "TextualBody", "value": "REPORT OF THE COMMITTEE", "format": "text/plain" },
      "target": "https://example.org/canvas/1#xywh=490,502,2376,100"
    }
  ]
}
```

---

### GET /annotations/words/v1/{n}/{**id}

Returns word-level annotations for canvas `{n}` of job `{**id}`. Each annotation covers one
word; its target is that word's individual bounding box (or time range for temporal canvases).

**Responses** — same as lines endpoint above.

**Example response**

```json
{
  "@context": [
    "http://iiif.io/api/presentation/3/context.json",
    "https://iiif.io/api/extension/text-granularity/context.json"
  ],
  "id": "https://search.example.org/annotations/words/v1/0/my-collection/my-book",
  "type": "AnnotationPage",
  "textGranularity": "word",
  "items": [
    {
      "id": "https://search.example.org/annotations/words/v1/0/my-collection/my-book/anno/0",
      "type": "Annotation",
      "motivation": "supplementing",
      "body": { "type": "TextualBody", "value": "REPORT", "format": "text/plain" },
      "target": "https://example.org/canvas/1#xywh=490,502,500,100"
    }
  ]
}
```

### URL structure note

The canvas index `{n}` precedes the job `{**id}` in the path (e.g.
`/annotations/lines/v1/0/my-collection/my-book`). This ordering is a routing constraint:
ASP.NET minimal APIs require the catch-all segment `{**id}` to be last, so the integer index
must come first. Callers who harvest these pages and serve them independently are expected to
rewrite the URLs to whatever scheme suits their system.

---

## Derived outputs

### GET /text-augmented/v3/{**id}

Returns the original IIIF Presentation 3 Manifest stored by the Builder API, augmented with:

- **`id`** replaced by the URL of this endpoint.
- **`service`** array prepended with `SearchService2` (v2 first) and `SearchService1` (v1) descriptors, each containing a nested autocomplete service.
- **`rendering`** array prepended with a plain-text link and, for image-based manifests, a PDF download link.
- **`annotations`** (manifest-level) prepended with a reference to the identified figures `AnnotationPage`, when figures were found during the build.
- Each canvas's **`annotations`** array prepended with references to the line-level and word-level annotation pages for that canvas (only for canvases that have at least one word).

This is the recommended way to give a viewer working search without modifying the authoritative
Manifest. Load the URL directly in any IIIF v3 viewer that supports content search.

**Example service block injected at the top of `service`**

```json
{
  "id": "https://search.example.org/search/v2/my-collection/my-book",
  "type": "SearchService2",
  "service": [
    {
      "id": "https://search.example.org/autocomplete/v2/my-collection/my-book",
      "type": "AutoCompleteService2"
    }
  ]
}
```

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | Augmented Manifest JSON. |
| `404 Not Found` | No stored Manifest for this `id` (only available when the job was submitted with `sourceUri`). |

---

### GET /text/v1/{**id}

Returns the raw (un-normalised) full text of the item as `text/plain`. Useful for downstream
processing, accessibility, or debugging.

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | Plain text body. |
| `404 Not Found` | No text index for this `id`. |

---

### GET /pdf/v1/{**id}

Returns a PDF rendering of the item. The PDF is generated on the first request and stored; 
subsequent requests are served from storage. An optional `.pdf` suffix is accepted for
browser-friendly save-as filenames (e.g. `/pdf/v1/my/book.pdf`).

Only available for jobs whose source contains at least one image-based canvas. Temporal-only
sources (audio/video with VTT captions) return 404.

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | PDF stream (`application/pdf`). |
| `404 Not Found` | No text index for this `id`, or the source is temporal-only. |

---

### POST /pdf/v1/{**id}

Triggers asynchronous pre-generation of a PDF without waiting for the result. Intended for bulk
pre-warming.

**Responses**

| Status | Meaning |
|---|---|
| `202 Accepted` | PDF generation started. `Location` header contains the GET URL. |
| `200 OK` | PDF already exists. Body contains `{ "location": "..." }` with the download URL. |
| `404 Not Found` | No text index for this `id`. |

---

### GET /identified/figures/{**id}

Returns a IIIF `AnnotationPage` whose `items` are `painting` annotations targeting canvas
regions identified as figures, tables, or illustrations (from ALTO `ComposedBlock` elements).

**Response** — a IIIF Presentation 3 `AnnotationPage`:

```json
{
  "id": "https://search.example.org/identified/figures/my-collection/my-book",
  "type": "AnnotationPage",
  "items": [
    {
      "id": "https://search.example.org/identified/figures/my-collection/my-book/0",
      "type": "Annotation",
      "motivation": "painting",
      "target": "https://example.org/canvas/5#xywh=100,200,800,600"
    }
  ]
}
```

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | AnnotationPage with figure annotations. |
| `404 Not Found` | No figures stored for this `id` (either no index, or no figures were detected). |

---

## Linking search services to your own Manifest

If you maintain your own authoritative Manifest and want to add search service links rather than
using the `/text-augmented/v3/` proxy, add the following to your Manifest's `service` array:

```json
{
  "id": "https://search.example.org/search/v2/my-collection/my-book",
  "type": "SearchService2",
  "service": [
    {
      "id": "https://search.example.org/autocomplete/v2/my-collection/my-book",
      "type": "AutoCompleteService2"
    }
  ]
}
```

For older viewers also add the v1 service:

```json
{
  "id": "https://search.example.org/search/v1/my-collection/my-book",
  "type": "SearchService1",
  "service": [
    {
      "id": "https://search.example.org/autocomplete/v1/my-collection/my-book",
      "type": "AutoCompleteService1"
    }
  ]
}
```

---

## Configuration

Search API configuration lives under the `TextServices` key in `appsettings.json`:

```json
{
  "TextServices": {
    "BaseUrl": "https://search.example.org",
    "CacheSlidingExpirationMinutes": 30,
    "CacheAbsoluteExpirationHours": 4,
    "CacheMaxEntries": 20,
    "StorageRootPath": "/data/textservices"
  },
  "CorsAllowedOrigins": ["https://viewer.example.org"]
}
```

| Setting | Default | Description |
|---|---|---|
| `BaseUrl` | `""` | Public base URL of this API. Required when running behind a reverse proxy; without it, self-referencing URLs in responses will use the incoming `Host` header, which may be internal. |
| `CacheSlidingExpirationMinutes` | `30` | How long a text object stays in the memory cache after its last access. |
| `CacheAbsoluteExpirationHours` | `4` | Hard upper limit on cache lifetime, regardless of access frequency. Prevents large objects from living in the LOH indefinitely. |
| `CacheMaxEntries` | `20` | Maximum number of Text (and AutoComplete) objects held in memory simultaneously. Each object counts as one slot; LRU eviction applies when the limit is reached. Budget approximately 30–40 MB per large text when sizing container memory. |
| `StorageRootPath` | `textservices-data` | Root directory of the text artefact store. Must point to the same location as the Builder API's `Storage:RootPath`. |
| `CorsAllowedOrigins` | `[]` | Allowed CORS origins. Empty array disables CORS. |
