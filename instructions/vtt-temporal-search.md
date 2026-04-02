# VTT / Temporal Search — Design Document

## Background

IIIF Search API v2 supports time-based annotations. Where a spatial search result
targets a canvas region with `#xywh=x,y,w,h`, a temporal result targets a time range
with `#t=start,end` (seconds). This is the natural way to surface transcript search
results against audio and video canvases.

WebVTT (`.vtt`) is the standard caption/subtitle format for web video. As ALTO is to
images (words with bounding boxes), VTT is to video (cues with time ranges). Both
describe the same thing — positioned text — just in different coordinate systems.

Reference: IIIF Search API v2 temporal target —
https://iiif.io/api/search/2.0/#temporal-selector

---

## Core insight: the search engine is coordinate-agnostic

`Text.Search()` operates entirely on flat strings (`NormalisedFullText`) and a
position index (`Words` keyed by character offset). It knows nothing about pixels or
time. The same `IndexOf` + coalesce-by-`Li` algorithm works for temporal content
without any changes.

`AutoComplete` likewise operates only on `ContentNorm` — entirely unaffected.

The only things that are spatial-specific today are:
- `Word.X`, `Word.Y`, `Word.W`, `Word.H` — bounding box in canvas pixels
- `ResultRect.X/Y/W/H` — aggregated bounding box, used by the Search API to build `#xywh=` targets
- `Image.ImageIdentifier` has no concept of whether its canvas carries spatial or temporal content

The design below adds temporal position fields alongside the existing spatial ones and
adds a per-canvas flag so the Search API knows which kind of fragment to emit. The
spatial path is unchanged in every detail.

---

## Model changes

### `Image` — add `IsTemporalContent` (ProtoMember 3)

```csharp
[ProtoMember(3)] public bool IsTemporalContent { get; set; }
```

**Per-canvas flag, not document-wide.** This correctly handles mixed manifests where
some canvases carry images with ALTO and others carry video with VTT — both kinds
appear in the same `Text` object and the same `Images[]` array, and each canvas
records its own coordinate type. Existing serialised files default `false` = spatial.

### `Word` — add `StartMs` / `EndMs` (ProtoMembers 13, 14)

```csharp
[ProtoMember(13)] public int StartMs { get; set; }
[ProtoMember(14)] public int EndMs   { get; set; }
```

- **Spatial words**: `X/Y/W/H` populated; `StartMs`/`EndMs` = 0.
- **Temporal words**: `X=Y=W=H=0`; `StartMs`/`EndMs` populated (milliseconds).

Milliseconds rather than seconds keeps them as integers in the protobuf. Existing
serialised files default both new fields to 0 — backward compatible.

### `ResultRect` — add `StartMs` / `EndMs` (not stored)

These are computed fields, populated at search time from the constituent `Word`
objects (same as `X/Y/W/H` today). During coalescing, take `Min(StartMs)` and
`Max(EndMs)` across all words in the result. In practice, all words in a VTT cue
share the same time range, so this is a no-op — but the min/max approach is correct
for the general case.

---

## Accumulator changes

### `TextAccumulator.BeginPage` — add optional `isTemporalContent`

```csharp
public void BeginPage(string imageIdentifier, bool isTemporalContent = false)
```

Default `false` — all existing call sites are unaffected.

### `TextAccumulator.AddWord` — new temporal overload

Keep the existing spatial signature unchanged:

```csharp
public void AddWord(string raw, string norm, int x, int y, int w, int h, int spaceAfter = 0)
```

Add a new overload for temporal words:

```csharp
public void AddWord(string raw, string norm, int startMs, int endMs)
```

Sets `X=Y=W=H=Sp=0`, populates `StartMs`/`EndMs`. All sequencing fields (`Wd`, `Li`,
`Idx`, `PosNorm`, `PosRaw`) are assigned identically to the spatial path.

---

## VTT provider

New `VttTextFormatProvider` implementing `ITextFormatProvider`. Called by `TextBuilder`
in the same way as `AltoTextFormatProvider`.

### Word vs cue granularity

VTT cues contain phrases (multiple words). Individual words within a cue are indexed
separately rather than treating the whole cue as one token. Reasons:

- Multi-word queries (`"prime minister"`) match correctly within a cue using the
  existing positional logic.
- Cross-cue matches produce separate `ResultRect`s (one per cue), each with its own
  time annotation — which is the correct IIIF Search v2 behaviour.
- It mirrors the ALTO approach: ALTO indexes individual words that share a text line;
  VTT indexes individual words that share a time range.

All words within a single cue receive the same `StartMs`/`EndMs` and the same `Li`
value (line number). Each new cue calls `NextLine()` before indexing its words, so
words from different cues get different `Li` values and are not coalesced across cue
boundaries.

### Parsing outline

```
WEBVTT

00:00:05.000 --> 00:00:08.500
This is the first cue.

00:00:10.000 --> 00:00:15.000
Another cue here.
```

For each cue:
1. Parse `startTime` and `endTime` → integer milliseconds.
2. Call `accumulator.NextLine()`.
3. Split the cue text into words (whitespace split, strip VTT markup tags).
4. For each word: `accumulator.AddWord(raw, norm, startMs, endMs)`.

Cue-level metadata (speaker labels, positioning directives in `<>` tags) is stripped;
only the plain text content is indexed.

### ITextFormatProvider contract

`VttTextFormatProvider.Process(string imageIdentifier, int canvasWidth, int canvasHeight, XElement xml, TextAccumulator accumulator)` — but note that VTT is not XML. Two options:

**Option A**: Change `ITextFormatProvider` to accept `string rawContent` instead of
`XElement`. This is a breaking change to the interface and forces ALTO/hOCR to parse
their own XML rather than receiving a pre-parsed `XElement`.

**Option B**: Keep `ITextFormatProvider` taking `XElement` and add a separate
`ITranscriptFormatProvider` interface for text-based formats (VTT, SRT) that accepts
`string rawContent`. `TextBuilder` is extended to support both.

**Option B is preferred** — it avoids touching the established XML-based interface and
reflects the genuine difference in source format. The builder pipeline then has two
provider types: XML-based (`ITextFormatProvider`) and text-based
(`ITranscriptFormatProvider`).

---

## Search API changes

### Fragment type selection

In the search result → IIIF annotation mapping, check `text.Images[rect.Idx].IsTemporalContent`:

```
false  →  canvasId + "#xywh=" + rect.X + "," + rect.Y + "," + rect.W + "," + rect.H
true   →  canvasId + "#t=" + (rect.StartMs / 1000.0) + "," + (rect.EndMs / 1000.0)
```

Seconds (not milliseconds) in the `#t=` fragment, per the IIIF Media Fragments spec.

### IIIF Search v2 annotation shape

For temporal results the annotation body carries the matched text and the target
points to the time range:

```json
{
  "id": "https://example.org/search/v2/my-id?q=term#0",
  "type": "Annotation",
  "motivation": "supplementing",
  "body": {
    "type": "TextualBody",
    "value": "matched cue text",
    "format": "text/plain"
  },
  "target": "https://example.org/canvas/1#t=5.0,8.5"
}
```

This is valid IIIF Search API v2 — the same annotation structure, just a different
fragment selector. No new response envelope shape is needed.

---

## Builder API changes

### VTT seeAlso detection

`ManifestReducer.TryGetTextSource` currently detects ALTO and hOCR by profile/label.
Add VTT detection:

```csharp
ContainsIgnoreCase(profile, "text/vtt") ||
ContainsIgnoreCase(profile, "vtt")      ||
ContainsIgnoreCase(label,   "vtt")      ||
ContainsIgnoreCase(label,   "webvtt")   ||
ContainsIgnoreCase(label,   "transcript")
```

### Fetching VTT

ALTO files are fetched as raw bytes and parsed as XML. VTT files are fetched as plain
text (`response.Content.ReadAsStringAsync()`). The `PageInstruction` already carries a
`Profile` string which `TextBuildJob` can use to route to the correct provider.

---

## What is not changed

| Component | Status |
|---|---|
| `Text.Search()` | Unchanged |
| `Text.Normalise()` | Unchanged |
| `AutoComplete` / `TextAccumulator.BuildAutoComplete` | Unchanged |
| `AddWord(raw, norm, x, y, w, h)` | Unchanged |
| `BeginPage(string id)` | Unchanged (new param is optional) |
| `AltoTextFormatProvider` | Unchanged |
| `HocrTextFormatProvider` | Unchanged |
| `ITextStore` | Unchanged — VTT-derived `Text` is stored under the same key as ALTO-derived `Text` |
| `FileSystemTextStore` / `S3TextStore` | Unchanged |
| Protobuf serialisation of existing files | Unchanged — new fields default to 0/false |
| Search API v1 spatial search | Unchanged |
| PDF derivative | Unchanged — skips canvases with no image body; VTT canvases have no image body |

---

## Suggested PR sequence

1. Model changes: `Image.IsTemporalContent`, `Word.StartMs`/`EndMs`, `ResultRect` temporal fields.
2. Accumulator: `BeginPage` flag + temporal `AddWord` overload.
3. `ITranscriptFormatProvider` interface + `VttTextFormatProvider`.
4. Builder API: VTT seeAlso detection, text fetch path, provider routing.
5. Search API: temporal fragment emission in IIIF Search v2 handler.
