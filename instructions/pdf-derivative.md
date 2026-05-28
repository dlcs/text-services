# PDF Derivative — Design

## Overview

The PDF derivative provides a searchable PDF for each processed job: one page per canvas, with the
canvas image as the background and an invisible text layer derived from the stored word positions.
The text layer uses PDF rendering mode 3 (Tr=3) — words are present in the content stream and
selectable/searchable but not visually rendered.

This is the "image + OCR overlay" pattern used by Tesseract PDF output and by Wellcome Collection
for their own PDF derivative. Our version differs: rather than running OCR, we use the word
positions already stored in the `Text` protobuf object, so the text layer is derived from the same
data that drives IIIF Search.

---

## Library

**iText 7 Community (AGPL v3)** — `itext7` NuGet package.

iText 7 is the only viable open-source .NET option: it exposes
`PdfCanvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE)` natively.
PdfSharp (MIT) has no text rendering mode API and would require unsupported raw content-stream
injection.

**Isolation**: iText 7 is referenced only by `TextServices.Pdf` (a dedicated class library).
Builder API and Search API reference `TextServices.Pdf`; the Core and Storage projects remain
iText-free. This contains the AGPL dependency and allows the PDF feature to be excluded from
deployments that don't need it.

---

## Architecture

PDF generation is **on-demand with lazy caching**:

- The Builder API job does **not** generate the PDF. Job time remains predictable.
- The Search API exposes two endpoints:
  - `GET /pdf/v1/{**id}` — synchronous. Generates if absent (blocking), serves on completion.
    This is the endpoint linked in the manifest `rendering` array.
  - `POST /pdf/v1/{**id}` — async trigger. Starts generation in the background and returns
    202 Accepted with a `Location` header. For machine-to-machine or bulk pre-generation workflows.
- Concurrent `GET` requests for the same key are serialised by `AsyncKeyedLock<string>`:
  one request generates, others wait and then serve from storage. No duplicate work.
- The PDF is stored via `ITextStore.SavePdf` / `LoadPdf` alongside the other derivatives.
  It is never held in the in-process memory cache (PDFs can be tens of MB).

---

## Storage

`ITextStore` gains:

```csharp
Task SavePdf(string key, Stream pdfStream);
Task<Stream?> LoadPdf(string key);
```

`FileSystemTextStore`: stored as `book.pdf` in the job directory.
`S3TextStore`: stored as `book.pdf` object with `Content-Type: application/pdf`.

Stream-based (not `byte[]`) to avoid loading the entire PDF into memory on serve.

---

## Text-augmented Manifest

`TextAugmentedQuery` adds the PDF rendering link **unconditionally** when `textStore.Exists(id)`
is true (i.e., text was built for the job). The PDF link appears in `rendering[0]` before the
plain text link, matching the Wellcome pattern. The link is present from the moment text
artefacts exist; the first click may be slow while generation occurs, but subsequent clicks serve
from storage.

---

## PDF Generation — `PdfBuilder`

`PdfBuilder` is a service in `TextServices.Pdf`. Its public entry point:

```csharp
Task BuildAsync(Text text, string manifestJson, HttpClient http, Stream output, CancellationToken ct);
```

### Per-page algorithm

For each canvas in `text.Images`:

**1. Find the painting annotation body**

Parse `manifestJson` (already stored, cheap) to locate the canvas with
`id == image.ImageIdentifier`. Walk to `canvas.items[0].items[0].body` (the painting
annotation body). If the body is an array, take the first item.

Skip the page if:
- No painting annotation body is found
- Body type is not `Image` (e.g., video, audio — `type` field, case-insensitive check)

**2. Choose image URL and dimensions**

Determine the *native* pixel dimensions of the image resource:
- Use `body.width` / `body.height` if present
- Otherwise fall back to `canvas.width` / `canvas.height` (may differ in scale — noted)

Check whether the longest edge exceeds **2000 px**:

| Condition | Image URL to use |
|---|---|
| Longest edge ≤ 2000 px (no service) | `body.id` directly |
| Longest edge ≤ 2000 px (has service, has `sizes`) | `body.id` directly (already within limit) |
| Longest edge > 2000 px, service has `sizes` | largest `sizes` entry with longest edge ≤ 2000 px: `{serviceId}/{w},{h}/...` |
| Longest edge > 2000 px, service present but no `sizes` | `{serviceId}/full/!2000,2000/0/default.jpg` |
| Longest edge > 2000 px, no service | `{serviceId}/full/!2000,2000/0/default.jpg` where serviceId = `body.id` |

The `sizes` array in an Image Service info.json (or inline in the manifest service descriptor)
contains pre-generated tile entries; using one avoids fractional scaling and is the
most cache-friendly approach.

**3. Fetch the image**

`HttpClient.GetStreamAsync(imageUrl)`. On failure: emit an image-free page (blank white) and
continue — do not abort the whole job.

**4. Size the PDF page**

Use the **fetched image's actual pixel dimensions** (from the HTTP response or from iText's
`ImageData` after loading) rather than the canvas or body dimensions.

Page size in points at **150 dpi**:

```
widthPt  = imageWidthPx  × (72.0 / 150.0)
heightPt = imageHeightPx × (72.0 / 150.0)
```

**5. Image layer**

Draw the image filling the entire page via `PdfCanvas.AddImage` (or `AddImageAt`).

**6. Text layer**

Get words for this page: `text.Words.Values.Where(w => w.Idx == pageIndex)`.

Scale factors (canvas coords → PDF points):

```
scaleX = widthPt  / canvasWidth
scaleY = heightPt / canvasHeight
```

where `canvasWidth` / `canvasHeight` come from the canvas element in the manifest.

For each word:

```
x_pdf    = word.X * scaleX
y_pdf    = heightPt - (word.Y + word.H) * scaleY   // Y-axis flip (PDF origin = bottom-left)
fontSize = word.H * scaleY
```

Horizontal stretch to fill bounding box:

```
targetWidthPt = word.W * scaleX
hScale        = (targetWidthPt / glyphWidthAtFontSize) * 100   // Tz percentage
```

iText 7 API:

```csharp
canvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE);
canvas.SetFontAndSize(font, fontSize);
canvas.SetHorizontalScaling(hScale);
canvas.MoveText(x_pdf, y_pdf);
canvas.ShowText(word.ContentRaw);
```

**7. Font**

Embed a Unicode TrueType font (e.g., Liberation Sans or the system Arial fallback).
For an invisible layer, visual quality is irrelevant; coverage of the character set is what
matters. `PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED)`.

**8. Pages with no words**

Canvases with no words in `text.Words` (e.g., sparse manifests with no ALTO for that page)
get an image-only page with no text layer. This is expected and not an error.

---

## Endpoints

### `GET /pdf/v1/{**id}`

1. `await textStore.LoadPdf(id)` — if non-null, stream directly to response (`application/pdf`,
   `Content-Disposition: attachment; filename="text.pdf"`).
2. If null: acquire `AsyncKeyedLock` for the key.
3. Double-check: load again inside lock (another request may have generated it while we waited).
4. If still null: call `PdfBuilder.BuildAsync(...)`, save via `textStore.SavePdf(id, ...)`.
5. Stream from storage.

### `POST /pdf/v1/{**id}`

1. `await textStore.LoadPdf(id)` — if non-null, return 200 (already done).
2. If generation is already in progress for this key (`AsyncKeyedLocker.IsInUse`), return 202.
3. Otherwise: call `PdfGenerationQueue.TryEnqueue(id)`.
   - Returns `true` → return 202 Accepted with `Location: /pdf/v1/{id}` and `Retry-After: 30`.
   - Returns `false` (queue full) → return 503 Service Unavailable with `Retry-After: 30`.

HTTP status mapping for the trigger endpoint:

| `PdfTriggerResult` | Status | Meaning |
|---|---|---|
| `AlreadyExists` | 200 OK | PDF present; `location` in body |
| `Queued` | 202 Accepted | Enqueued or in progress |
| `ServiceBusy` | 503 Service Unavailable | Queue full; retry shortly |
| `NotFound` | 404 Not Found | No text artefact or service disabled |

---

## Trigger Queue

The POST trigger uses a `Channel<string>` + `BackgroundService` + `SemaphoreSlim` stack to provide
backpressure without blocking the request thread.

```
PdfGenerationQueue        — bounded Channel<string>; TryEnqueue returns false when full
PdfGenerationBackgroundService — BackgroundService draining the queue; SemaphoreSlim bounds concurrency
PdfGenerationService      — shared generation logic used by both GET and background paths
```

**Layers and responsibilities:**

- `PdfGenerationQueue` (singleton): owns the channel. `TryEnqueue` returns `false` when full
  (using `BoundedChannelFullMode.Wait` so `Channel.Writer.TryWrite` gives a reliable false).
- `PdfGenerationBackgroundService` (hosted service): reads IDs with `ReadAllAsync`,
  acquires the semaphore before launching each `ProcessAsync` task. Concurrency is bounded
  by `PdfTriggerMaxConcurrency`, which also bounds peak `MemoryStream` memory.
- `PdfGenerationService` (singleton): acquires the `AsyncKeyedLocker` per-key, double-checks
  whether the PDF already exists, then generates. Called by both the GET handler (blocking) and
  the background service (fire-and-forget with `CancellationToken.None`).

**Config keys** (under `TextServices:`):

| Key | Default | Effect |
|---|---|---|
| `PdfTriggerQueueCapacity` | 50 | Max IDs waiting in the trigger queue before 503 |
| `PdfTriggerMaxConcurrency` | 2 | Max concurrent background PDF builds |

**Trade-offs:**

- Queue is in-process: contents are lost on pod restart. Acceptable because the synchronous
  `GET /pdf/v1/{**id}` endpoint is the guaranteed fallback — a lost queue entry simply
  means the next GET generates the PDF on demand.
- `CancellationToken.None` in background generation: in-flight builds survive ASP.NET shutdown
  signals. Since each build is bounded in duration by network and I/O timeouts, this is benign.

---

## Concurrency guard detail

```
Request A (GET, cold): PdfGenerationService acquires lock → generates → saves → releases → serves
Request B (GET, concurrent): waits on lock → load (now present) → serves
Request C (POST, cold): enqueued → returns 202
Request D (POST, queue full): TryEnqueue returns false → returns 503
Request E (GET, arrives while background task runs): waits on lock → serves when done
```

`AsyncKeyedLocker<string>` (singleton) is shared between `PdfGenerationService` and `TextCache`.

---

## Rendering link in text-augmented manifest

Added unconditionally when `textStore.Exists(id)` is true:

```json
{
  "id": "https://search.example.org/pdf/v1/some/id",
  "type": "Text",
  "label": { "en": ["Download as PDF"] },
  "format": "application/pdf"
}
```

Inserted at `rendering[0]` (PDF before plain text, matching Wellcome order).

---

## Demo viewer

No changes needed. `extractRendering()` already handles any number of entries;
the PDF link appears alongside the plain text link automatically.
