# VTT Temporal Search — Implementation Plan

Design document: `instructions/vtt-temporal-search.md`

---

## Overview

Five layers need touching, in dependency order:

1. Core models (`Word`, `Image`, `ResultRect`, `Text`)
2. Core accumulator (`TextAccumulator`)
3. Core provider layer (new `ITranscriptFormatProvider` + `VttTextFormatProvider`, extend `TextBuilder`)
4. Builder API (`ManifestReducer`, `IVttFetcher` + `VttFetcher`, `TextBuildJob`, `Program.cs`)
5. Search API (`SearchV2Query.cs` and `SearchV2Models.cs`)
6. Tests

---

## Step 1 — Core model changes

### 1a. `src/TextServices.Core/Models/Word.cs`

Add two new protobuf members after the existing `[ProtoMember(12)] PosRaw` property,
before the computed `LenNorm`/`LenRaw` properties:

```csharp
[ProtoMember(13)] public int StartMs { get; set; }
[ProtoMember(14)] public int EndMs   { get; set; }
```

Field numbers 13 and 14 are the next available (1–12 already assigned).
Existing serialised files default both new fields to 0 — backward compatible.
Spatial words will always have `StartMs = EndMs = 0`.

---

### 1b. `src/TextServices.Core/Models/Image.cs`

Add one new protobuf member after the existing `[ProtoMember(2)] ImageIdentifier`:

```csharp
[ProtoMember(3)] public bool IsTemporalContent { get; set; }
```

`false` is the protobuf-net default for `bool` — all existing files are unaffected.

---

### 1c. `src/TextServices.Core/Models/ResultRect.cs`

Add two non-persisted properties (no `[ProtoMember]`):

```csharp
public int StartMs { get; set; }
public int EndMs   { get; set; }
```

Placement: alongside `X`, `Y`, `W`, `H`.

Update the static `FromWord` factory to copy the new fields:

```csharp
StartMs = word.StartMs,
EndMs   = word.EndMs,
```

---

### 1d. `src/TextServices.Core/Models/Text.cs`

Inside `GetRectangles`, in the coalescing block that expands `current` when adjacent
same-line words are merged, add after the `current.W = ...` line:

```csharp
current.StartMs = Math.Min(current.StartMs, next.StartMs);
current.EndMs   = Math.Max(current.EndMs,   next.EndMs);
```

For spatial words `StartMs = EndMs = 0` throughout, so `Math.Min(0,0)` and
`Math.Max(0,0)` are correct no-ops. For temporal words `X = W = 0` throughout, so
the existing `current.W = (next.X + next.W) - current.X` evaluates to 0 — also
correct.

---

## Step 2 — Accumulator changes

### 2a. `src/TextServices.Core/Providers/TextAccumulator.cs`

**Change 1 — `BeginPage` optional parameter:**

```csharp
// Before
public void BeginPage(string imageIdentifier)

// After
public void BeginPage(string imageIdentifier, bool isTemporalContent = false)
```

In the body, add one line to the `Image` initialiser:

```csharp
_images.Add(new Image
{
    StartCharacter    = _normText.Length,
    ImageIdentifier   = imageIdentifier,
    IsTemporalContent = isTemporalContent,   // new
});
```

All existing callers pass no second argument and are unaffected.

**Change 2 — temporal `AddWord` overload:**

Add immediately after the existing spatial `AddWord` method:

```csharp
public void AddWord(string contentRaw, string contentNorm, int startMs, int endMs)
```

Implementation is identical to the spatial overload except:
- `X = 0, Y = 0, W = 0, H = 0, Sp = 0`
- `StartMs = startMs, EndMs = endMs` on the `Word` object

All sequencing fields (`Wd`, `Li`, `Idx`, `PosNorm`, `PosRaw`) assigned identically.
The existing spatial `AddWord` signature is unchanged.

---

## Step 3 — New provider interface and VTT provider

### 3a. New file: `src/TextServices.Core/Providers/ITranscriptFormatProvider.cs`

```csharp
namespace TextServices.Core.Providers;

public interface ITranscriptFormatProvider
{
    bool Supports(string? profile, string? label);

    void ProcessPage(
        TextAccumulator accumulator,
        string          rawContent,
        string          imageIdentifier,
        int             canvasWidth,
        int             canvasHeight);
}
```

Parallel to `ITextFormatProvider` but takes `string rawContent` instead of
`XElement root`. `canvasWidth`/`canvasHeight` are included for interface symmetry
(VTT ignores them; a future SRT provider might use them for positioning hints).

---

### 3b. New file: `src/TextServices.Core/Providers/VttTextFormatProvider.cs`

Implements `ITranscriptFormatProvider`.

**`Supports` method:**

Returns `true` (case-insensitive) when any of:
- `profile` contains `"text/vtt"`
- `profile` contains `"vtt"`
- `label` contains `"vtt"`
- `label` contains `"webvtt"`
- `label` contains `"transcript"`

Must NOT return `true` when both profile and label are null (unlike
`AltoTextFormatProvider` which is the XML fallback).

**`ProcessPage` method:**

1. Call `accumulator.BeginPage(imageIdentifier, isTemporalContent: true)`.

2. Normalise line endings: `rawContent.ReplaceLineEndings("\n")`.

3. Strip leading BOM if present: `content.TrimStart('\uFEFF')`.

4. Split into lines. Skip the mandatory `WEBVTT` header line and all blank lines
   before the first cue.

5. For each cue block:
   a. Read until a timing line is found (matches `-->` separator).
      Skip any cue identifier line that precedes the timing line.
   b. Parse `startMs` and `endMs` from the timing line using a private helper
      `ParseVttTimestamp(string token) → int`:
      - Split on `:` and `.`.
      - Three-component form `HH:MM:SS.mmm`: `h*3_600_000 + m*60_000 + s*1000 + ms`
      - Two-component form `MM:SS.mmm`: `m*60_000 + s*1000 + ms`
      - On parse failure, return 0 (do not throw).
   c. Collect subsequent non-blank lines as the cue's text content.
   d. Strip VTT inline markup from the concatenated cue text: remove anything
      matching `<[^>]*>` (timestamp tags, speaker labels, class annotations).
   e. Call `accumulator.NextLine()` once for this cue.
   f. Split the stripped text by whitespace. For each token:
      - `raw = token`
      - `norm = Text.Normalise(token)`
      - If `norm` is empty, skip.
      - Call `accumulator.AddWord(raw, norm, startMs, endMs)`.

6. No composed blocks are emitted.

**Key note on multi-line cues:** A cue whose text spans multiple text lines calls
`NextLine()` only once. All its words share the same `Li`, so they coalesce
correctly into a single result rect covering the cue's time range. Concatenate all
the cue's text lines before splitting into words — do not call `NextLine()` per
text line.

---

### 3c. `src/TextServices.Core/Providers/TextBuilder.cs`

**Three additive changes:**

**1.** Add a second field alongside `_providers`:

```csharp
private readonly IReadOnlyList<ITranscriptFormatProvider> _transcriptProviders;
```

**2.** Update constructors:

```csharp
// No-arg constructor — gains VttTextFormatProvider as default transcript provider
public TextBuilder()
    : this(
        [new AltoTextFormatProvider(), new HocrTextFormatProvider()],
        [new VttTextFormatProvider()])
    { }

// Explicit constructor — second param optional for backward compatibility
public TextBuilder(
    IReadOnlyList<ITextFormatProvider>        providers,
    IReadOnlyList<ITranscriptFormatProvider>? transcriptProviders = null)
{
    _providers           = providers;
    _transcriptProviders = transcriptProviders ?? [];
}
```

**3.** New `AddTranscriptPage` method (mirrors existing `AddPage`):

```csharp
public void AddTranscriptPage(
    string  id,
    int     canvasWidth,
    int     canvasHeight,
    string? rawContent,
    string? profile = null,
    string? label   = null)
```

Implementation:
- Return immediately if `rawContent` is null.
- Find the first `ITranscriptFormatProvider` where `Supports(profile, label)` is true.
- If none found, return.
- Call `provider.ProcessPage(_accumulator, rawContent, id, canvasWidth, canvasHeight)`.

The existing `AddPage(...)` method is not changed.

---

## Step 4 — Builder API changes

### 4a. `src/TextServices.Builder.Api/Services/ManifestReducer.cs`

**Change 1 — extend `IsRecognisedTextFormat` to check `format` as well:**

Both `seeAlso` entries and annotation bodies can carry `format`, `profile`, and
`label` as independent signals. `profile` qualifies the format (e.g.,
`format: "application/xml"` + `profile: "http://www.loc.gov/standards/alto/ns-v3#"`)
but either may appear alone. Detection must check all three.

Extend the signature to:
```csharp
private static bool IsRecognisedTextFormat(string? profile, string? format, string? label)
```

And update the body to cover all three, adding a new `IsVttFormat` helper:

```csharp
private static bool IsVttFormat(string? profile, string? format, string? label) =>
    ContainsIgnoreCase(profile, "text/vtt")  || ContainsIgnoreCase(format, "text/vtt") ||
    ContainsIgnoreCase(profile, "vtt")       || ContainsIgnoreCase(format, "vtt")      ||
    ContainsIgnoreCase(label,   "vtt")       || ContainsIgnoreCase(label,  "webvtt")   ||
    ContainsIgnoreCase(label,   "transcript");

private static bool IsRecognisedTextFormat(string? profile, string? format, string? label) =>
    ContainsIgnoreCase(profile, "alto") || ContainsIgnoreCase(format, "alto") || ContainsIgnoreCase(label, "alto") ||
    ContainsIgnoreCase(profile, "hocr") || ContainsIgnoreCase(format, "hocr") || ContainsIgnoreCase(label, "hocr") ||
    IsVttFormat(profile, format, label);
```

Update all call sites of `IsRecognisedTextFormat` and `IsVttFormat` to pass all three arguments.

**Change 2 — extend `TryGetTextSource` to extract `format`:**

Currently `TryGetTextSource` reads only `profile` and `label` from a seeAlso item.
Add extraction of `format`:

```csharp
var format  = item.TryGetProperty("format",  out var f) ? f.GetString() : null;
```

Pass all three to `IsRecognisedTextFormat(profile, format, label)` and carry `format`
into the `TextSource` record so that `IsVttFormat` can be called on the result later.
The `TextSource` record gains a `Format` field:

```csharp
private record TextSource(string Uri, string? Profile, string? Format, string? Label);
```

Existing callers of `TextSource` that only use `Uri` are unaffected. The `IsVttFormat`
check in the `Reduce` method becomes `IsVttFormat(source.Profile, source.Format, source.Label)`.

**Change 3 — extend `FindTextSource` to check `annotations`:**

The current `FindTextSource` only looks at `canvas.seeAlso`. Text resources (ALTO,
hOCR, VTT) can also appear as annotations with `motivation: supplementing` in the
canvas's `annotations` array. Either location can carry any recognised format.

Update `FindTextSource` to try both, preferring `seeAlso`:

```csharp
private static TextSource? FindTextSource(JsonElement canvas)
{
    // 1. Try seeAlso (existing path)
    if (canvas.TryGetProperty("seeAlso", out var seeAlso))
    {
        var source = seeAlso.ValueKind switch
        {
            JsonValueKind.Array  => FindTextSourceInArray(seeAlso),
            JsonValueKind.Object => TryGetTextSource(seeAlso),
            _                    => null,
        };
        if (source != null) return source;
    }

    // 2. Fall back to supplementing annotations
    return FindTextSourceInAnnotations(canvas);
}
```

Add new private method `FindTextSourceInAnnotations(JsonElement canvas)`:

```
- If canvas has no "annotations" property → return null.
- "annotations" is an array of AnnotationPages.
- For each AnnotationPage that has an "items" array (skip externally-referenced pages
  with only "id"):
    - For each item (Annotation):
        - Check motivation "supplementing" using HasSupplementingMotivation().
        - Get body (may be object or array — take first object if array).
        - Extract body.format, body.profile (if present), body.label (via ExtractLabelText).
        - Call IsRecognisedTextFormat(profile, format, label).
        - If recognised: extract body.id as URI → return new TextSource(uri, profile, format, label).
- Return null if no match found.
```

Add private helper `HasSupplementingMotivation(JsonElement annotation)`:

```
- Get annotation["motivation"].
- If it's a string: return true if == "supplementing" (case-insensitive).
- If it's an array: return true if any element equals "supplementing".
- Otherwise: return false.
```

**Change 4 — include temporal canvases:**

(Same as previously described — now using updated `IsVttFormat` signature.)

VTT canvases are commonly time-only (they have `duration` but no `width`/`height`).
Replace the current skip logic:

- If the canvas has both `width` and `height`: existing path.
- Else if the canvas has `duration` AND `FindTextSource` returns a VTT source
  (checked via `IsVttFormat(source.Profile, source.Format, source.Label)`):
  include with `Width = 0, Height = 0`.
- Else: skip.

Note: call `FindTextSource` once and reuse the result in both the format check and
the `PageInstruction` — avoid calling it twice.

**Change 3 — include temporal canvases:**

VTT canvases are commonly time-only (they have `duration` but no `width`/`height`).
The current skip logic is:

```csharp
if (!canvas.TryGetProperty("width",  out var w) ||
    !canvas.TryGetProperty("height", out var h))
    continue;
```

Replace with two-branch logic:

- If the canvas has both `width` and `height`: existing path, `Width = w`, `Height = h`.
- Else if the canvas has `duration` AND `FindTextSource` returns a VTT source
  (checked via `IsVttFormat(source.Profile, source.Format, source.Label)`):
  include with `Width = 0, Height = 0`.
- Else: skip (current behaviour for audio-only or dimensionless canvases without VTT).

Note: call `FindTextSource` once and reuse the result in both the format check and
the `PageInstruction` — avoid calling it twice.

Preserve the existing test `Reduce_CanvasWithWidthHeightAndDuration_Included` (a
canvas that has both spatial dimensions and duration is included as before).

---

### 4b. New file: `src/TextServices.Builder.Api/Services/IVttFetcher.cs`

```csharp
namespace TextServices.Builder.Api.Services;

public interface IVttFetcher
{
    Task<string?> FetchAsync(string uri, CancellationToken ct = default);
}
```

Returns raw VTT as a `string`, `null` for HTTP 404. Throws for other failures.

---

### 4c. New file: `src/TextServices.Builder.Api/Services/VttFetcher.cs`

```csharp
public class VttFetcher(IHttpClientFactory httpClientFactory) : IVttFetcher
{
    public async Task<string?> FetchAsync(string uri, CancellationToken ct = default)
    {
        var client   = httpClientFactory.CreateClient("Vtt");
        var response = await client.GetAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

Uses a named `HttpClient` called `"Vtt"` (registered in `Program.cs`).

---

### 4d. `src/TextServices.Builder.Api/Jobs/TextBuildJob.cs`

**Change 1 — new `FetchedPage` record:**

Add a private record that unifies both fetch result types:

```csharp
private record FetchedPage(
    PageInstruction Page,
    XElement?       Xml,
    string?         Vtt,
    string?         Error);
```

**Change 2 — constructor:**

Add `IVttFetcher vttFetcher` as a new primary constructor parameter.

**Change 3 — `IsVttPage` helper:**

`PageInstruction` carries `Profile`, `Format` (add this field — see below), and
`Label` from the detected `TextSource`. All three are checked:

```csharp
private static bool IsVttPage(PageInstruction page) =>
    ContainsIgnoreCase(page.Profile, "text/vtt")  || ContainsIgnoreCase(page.Format, "text/vtt") ||
    ContainsIgnoreCase(page.Profile, "vtt")        || ContainsIgnoreCase(page.Format, "vtt")      ||
    ContainsIgnoreCase(page.Label,   "vtt")        ||
    ContainsIgnoreCase(page.Label,   "webvtt")     ||
    ContainsIgnoreCase(page.Label,   "transcript");
```

**`PageInstruction` must gain a `Format` field** (alongside the existing `Profile`
and `Label`). `ManifestReducer.Reduce` populates it from `source.Format`.
This is an additive change to the existing model class.

**Change 4 — `ProcessPages` method:**

Replace the two separate fetch + build loops with a unified approach:

1. For each page, dispatch to either `FetchXmlWithSemaphoreAsync` or
   `FetchVttWithSemaphoreAsync` depending on `IsVttPage(page)`. Both return
   a `FetchedPage`. Both share the same `SemaphoreSlim`.

2. `Task.WhenAll` over all fetch tasks (preserving original order).

3. In the `TextBuilder` loop:
   - `fetched.Xml != null` → `textBuilder.AddPage(...)`  (existing path)
   - `fetched.Vtt != null` → `textBuilder.AddTranscriptPage(...)` (new path)
   - Both null → sparse/errored, skip.

The `FetchXmlWithSemaphoreAsync` is the existing `FetchWithSemaphoreAsync` renamed
and returning `FetchedPage` instead of a tuple.

`FetchVttWithSemaphoreAsync` mirrors it: acquires the semaphore, calls
`vttFetcher.FetchAsync(page.Text, token)`, returns
`new FetchedPage(page, null, vttText, null)` on success,
`new FetchedPage(page, null, null, error)` on exception.

---

### 4e. `src/TextServices.Builder.Api/Program.cs`

Two additions:

```csharp
builder.Services.AddScoped<IVttFetcher, VttFetcher>();

builder.Services.AddHttpClient("Vtt", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TextServices/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

---

## Step 5 — Search API changes

### 5a. `src/TextServices.Search.Api/Features/Search/SearchV2Models.cs`

The `PaintingAnnotationV2` type has `Motivation` as a get-only property with value
`"painting"`. Change it to be settable:

```csharp
// Before
public string Motivation { get; } = "painting";

// After
public string Motivation { get; set; } = "painting";
```

This is a one-word change that keeps all spatial annotations using `"painting"` by
default while allowing the handler to override to `"supplementing"` for temporal
results.

---

### 5b. `src/TextServices.Search.Api/Features/Search/SearchV2Query.cs`

**Change 1 — add `BuildTarget` helper:**

```csharp
private static string BuildTarget(string canvasId, Text text, ResultRect rect)
{
    if (text.Images[rect.Idx].IsTemporalContent)
    {
        var start = (rect.StartMs / 1000.0).ToString(CultureInfo.InvariantCulture);
        var end   = (rect.EndMs   / 1000.0).ToString(CultureInfo.InvariantCulture);
        return $"{canvasId}#t={start},{end}";
    }
    return $"{canvasId}#xywh={rect.X},{rect.Y},{rect.W},{rect.H}";
}
```

Use `CultureInfo.InvariantCulture` to avoid locale-dependent decimal separators
in the `#t=` fragment (e.g., `5,0` on a German locale vs `5.0`).

**Change 2 — use the helper and set motivation:**

Where the handler currently constructs each annotation:
- Replace the inline `#xywh=` string with a call to `BuildTarget(canvasId, text, rect)`.
- Set `Motivation = text.Images[rect.Idx].IsTemporalContent ? "supplementing" : "painting"`.

**Change 3 — temporal annotation ID:**

Annotation IDs must be unique and stable. For temporal results, use:
```csharp
$"{selfUrl}/anno/h{rect.Hit}i{rect.Idx}-t{rect.StartMs},{rect.EndMs}"
```

For spatial results, keep the existing coordinate-based format.

**`SearchQuery.cs` (v1) — no change.** Add a comment noting that temporal canvases
are only correctly represented in v2 responses (v1 would emit `#xywh=0,0,0,0`).

---

## Step 6 — Tests

### 6a. New file: `src/TextServices.Tests/Core/VttParsingTests.cs`

Unit tests for `VttTextFormatProvider`. Call `provider.ProcessPage(acc, vttString, id, 0, 0)` and assert on `acc.Build().Text.Words.Values`.

| Test | Verifies |
|---|---|
| `ProcessPage_BasicCue_IndexesWords` | Two words produced with correct `StartMs`/`EndMs` |
| `ProcessPage_MultipleCues_DifferentLiValues` | Words from separate cues have different `Li` |
| `ProcessPage_WithinCueWords_SameLi` | Words within one cue share `Li` |
| `ProcessPage_TimestampParsing_HhMmSsMs` | `00:01:23.456` → 83456 ms |
| `ProcessPage_TimestampParsing_MmSsMs` | `01:23.456` → 83456 ms |
| `ProcessPage_StripsCueTags` | `<c>hello</c>` indexes as `"hello"` |
| `ProcessPage_StripsTimestampTags` | `<00:00:01.000>word` indexes as `"word"` |
| `ProcessPage_EmptyCue_Skipped` | Whitespace-only cue produces no words |
| `ProcessPage_CueIdentifier_Ignored` | Optional cue ID line before timing line handled |
| `ProcessPage_MultiLineCue_AllWordsIndexed` | Two-line cue indexes all words under same `Li` |
| `ProcessPage_SetsIsTemporalContent` | Resulting `Image.IsTemporalContent` is `true` |
| `ProcessPage_WindowsLineEndings` | `\r\n` files handled identically to `\n` |
| `ProcessPage_BomStripped` | Leading `\uFEFF` does not break parsing |
| `ProcessPage_MalformedTimestamp_DoesNotThrow` | Unparseable timestamp produces words with `StartMs = EndMs = 0`, no exception |

---

### 6b. New file: `src/TextServices.Tests/Core/VttTemporalSearchTests.cs`

Integration tests for the full VTT → `TextBuilder` → `Text.Search` → `ResultRect` pipeline.

| Test | Verifies |
|---|---|
| `Search_TemporalWord_ReturnsStartMsEndMs` | `ResultRect.StartMs`/`EndMs` populated correctly |
| `Search_TemporalPhrase_CoalescesWithinCue` | Multi-word phrase in one cue → one `ResultRect` with correct time range |
| `Search_TemporalCrossCueBoundary_TwoRects` | Phrase spanning two cues → two `ResultRect`s with different `Hit` values |
| `Search_TemporalResultRect_ZeroSpatialCoords` | `X`, `Y`, `W`, `H` all 0 for temporal results |
| `Search_MixedSpatialAndTemporalCanvases` | Canvas 0 (ALTO) has `X/Y/W/H` non-zero; canvas 1 (VTT) has `StartMs/EndMs` non-zero |

For the mixed canvas test: call `textBuilder.AddPage(...)` for the ALTO canvas and `textBuilder.AddTranscriptPage(...)` for the VTT canvas. Assert `text.Images[0].IsTemporalContent == false` and `text.Images[1].IsTemporalContent == true`.

---

### 6c. Additions to `src/TextServices.Tests/BuilderApi/ManifestReducerTests.cs`

Add to the existing test class (no new file):

**seeAlso detection (existing location):**

| Test | Verifies |
|---|---|
| `Reduce_VttByProfile_DetectsLink` | `seeAlso profile: "text/vtt"` detected |
| `Reduce_VttByFormat_DetectsLink` | `seeAlso format: "text/vtt"` (with no profile) detected |
| `Reduce_VttByLabel_Transcript_SeeAlso` | `seeAlso label: "transcript"` detected |
| `Reduce_VttByLabel_WebVTT_CaseInsensitive` | `seeAlso label: "WebVTT"` detected |

**Supplementing annotation detection (new):**

| Test | Verifies |
|---|---|
| `Reduce_VttAnnotation_ByFormat_DetectsLink` | `annotations[motivation=supplementing].body.format = "text/vtt"` detected |
| `Reduce_VttAnnotation_ByLabel_DetectsLink` | `annotations[].body.label = "Captions in WebVTT format"` detected |
| `Reduce_AltoAnnotation_ByLabel_DetectsLink` | ALTO supplied as supplementing annotation is also detected |
| `Reduce_AnnotationMotivationArray_Detected` | `motivation: ["supplementing"]` (array form) is detected |
| `Reduce_AnnotationExternalPage_Skipped` | AnnotationPage with only `id` (no `items`) is silently skipped |
| `Reduce_SeeAlsoPreferredOverAnnotation` | Canvas with both seeAlso ALTO and supplementing VTT annotation → seeAlso result returned |
| `Reduce_AnnotationFallsBackWhenNoSeeAlso` | Canvas with no seeAlso but supplementing VTT annotation → annotation result returned |

**Temporal canvas inclusion:**

| Test | Verifies |
|---|---|
| `Reduce_TemporalCanvas_VttSeeAlso_Included` | Duration-only canvas with VTT seeAlso → `Width=0, Height=0` |
| `Reduce_TemporalCanvas_VttAnnotation_Included` | Duration-only canvas with VTT supplementing annotation → `Width=0, Height=0` |
| `Reduce_TemporalCanvas_NoVttSource_Skipped` | Duration-only canvas with no recognised text source → skipped |
| `Reduce_MixedManifest_SpatialAndTemporal` | One image canvas (ALTO seeAlso) + one video canvas (VTT annotation) → two `PageInstruction` entries |

Helpers: `CanvasWithVttSeeAlso(string id, double duration, string vttUri, string? profile, string? format, string? label)` and `CanvasWithVttAnnotation(string id, double duration, string vttUri, string? format, string? label)`. Both duration-only (no `width`/`height`).

---

### 6d. New file: `src/TextServices.Tests/SearchApi/SearchV2TemporalHandlerTests.cs`

| Test | Verifies |
|---|---|
| `Handle_TemporalHit_TargetUsesTimeFragment` | Target is `"...#t=5.0,8.5"`, not `"...#xywh=..."` |
| `Handle_TemporalHit_AnnotationIdUsesTemporalFormat` | Annotation `id` contains `"-t"` |
| `Handle_TemporalHit_MotivationIsSupplementing` | `motivation` is `"supplementing"` |
| `Handle_SpatialHit_Unchanged` | Spatial canvas still uses `#xywh=` and `"painting"` |
| `Handle_MixedManifest_CorrectFragmentPerCanvas` | Canvas 0 spatial → `#xywh`, canvas 1 temporal → `#t=` |
| `Handle_InvariantCultureDecimalSeparator` | `5000 ms` → `"5"` not `"5,0"` regardless of locale |

Helper: `BuildTemporalText(...)` — calls `TextAccumulator` directly with `BeginPage(id, isTemporalContent: true)` and the temporal `AddWord` overload.

---

### 6e. Additions to `src/TextServices.Tests/Core/ProtobufSerializationTests.cs`

Two new `[Fact]`s in the existing class:

- `Word_WithTemporalFields_RoundTrips` — `StartMs = 5000, EndMs = 8500` survive a protobuf serialise/deserialise cycle.
- `Image_WithIsTemporalContent_RoundTrips` — `IsTemporalContent = true` survives round-trip.

Guards against accidental field number changes in future.

---

### 6f. E2E additions (optional, last step)

In `src/TextServices.Tests.E2E/`:

1. `Fixtures/vtt-test/manifest.json` — synthetic IIIF v3 manifest, one duration-only canvas, VTT `seeAlso`.
2. `Fixtures/vtt-test/transcript.vtt` — minimal VTT with 3 known cues.
3. `Infrastructure/FixtureVttFetcher.cs` — implements `IVttFetcher`, serves local fixture file.
4. Register `FixtureVttFetcher` in `BuilderApiFactory.ConfigureTestServices`.
5. `BuildAndSearchTests.cs` — `BuildAndSearch_VttManifest_ReturnsTemporalFragments`:
   POST job → wait `Completed` → GET `search/v2/vtt-test?q=<known word>` → assert `target` contains `#t=` and `motivation` is `"supplementing"`.

---

## Implementation order

Execute in this order to avoid compile errors at each stage:

```
 1. Word.cs                        — ProtoMembers 13 & 14
 2. Image.cs                       — ProtoMember 3
 3. ResultRect.cs                  — StartMs/EndMs + update FromWord
 4. Text.cs                        — update GetRectangles coalescing
 5. TextAccumulator.cs             — BeginPage optional param + temporal AddWord
 6. ITranscriptFormatProvider.cs   — new interface
 7. VttTextFormatProvider.cs       — new implementation
 8. TextBuilder.cs                 — transcript providers + AddTranscriptPage
 9. PageInstruction.cs             — add Format field
 9. ManifestReducer.cs             — VTT/format detection + supplementing annotation + temporal canvas
10. IVttFetcher.cs                 — new interface
11. VttFetcher.cs                  — new implementation
12. TextBuildJob.cs                — FetchedPage record + dual-path processing
13. Program.cs (Builder)           — register IVttFetcher + "Vtt" HttpClient
14. SearchV2Models.cs              — Motivation settable
15. SearchV2Query.cs               — BuildTarget helper + temporal fragment emission
    Tests:
16. ProtobufSerializationTests.cs  — round-trip facts
17. VttParsingTests.cs             — new file
18. VttTemporalSearchTests.cs      — new file
19. ManifestReducerTests.cs        — VTT detection + temporal canvas tests
20. SearchV2TemporalHandlerTests.cs — new file
21. E2E fixture + infrastructure    — optional
```

---

## Gotchas and risks

**Coalescing formula for temporal words**

`Text.GetRectangles` expands `current.W = (next.X + next.W) - current.X`. For
temporal words `X = W = 0`, so this evaluates to 0 — harmless. For spatial words
`StartMs = EndMs = 0`, so `Math.Min(0,0)` and `Math.Max(0,0)` are also no-ops.
Explicitly verify in `VttTemporalSearchTests` that coalesced rects have correct
`StartMs`/`EndMs` and that spatial rects are unaffected.

**Multi-line cue text**

Call `NextLine()` once per cue, not once per text line within a cue. Concatenate all
the cue's text lines before splitting into words.

**Temporal canvases have `Width = Height = 0`**

`TextBuildJob.AddTranscriptPage` passes `canvasWidth = 0, canvasHeight = 0`. No
existing code guards on `width * height > 0`, but be careful when adding future
guards.

**`#t=` fragment uses seconds, not milliseconds**

`rect.StartMs / 1000.0` with `CultureInfo.InvariantCulture` formatting. Never use
integer division. Verify the invariant culture test case explicitly.

**Mixed manifests**

`text.Images[rect.Idx].IsTemporalContent` is per-canvas. Works correctly for a
manifest with both image and video canvases in the same `Text` object.

**Search API v1**

`SearchQuery.cs` (v1) is not updated. It would emit `#xywh=0,0,0,0` for temporal
results. Add a comment noting this limitation. VTT content is only correctly served
via the v2 handler.

**VTT detection terms — duplication**

The same terms appear in `ManifestReducer.IsVttFormat`, `TextBuildJob.IsVttPage`, and
`VttTextFormatProvider.Supports`. This is a conscious tradeoff (three small static
helpers vs a shared utility dependency). If a fourth location appears, extract to a
`TextFormatDetection` static class in `TextServices.Core.Providers`.

**`AltoTextFormatProvider.Supports` is the XML fallback**

It returns `true` when both `profile` and `label` are null. `VttTextFormatProvider`
must NOT. If `TextBuildJob` incorrectly routes a VTT page through `AddPage` instead
of `AddTranscriptPage`, the ALTO provider would attempt to parse VTT as XML and throw.
Guard is the `IsVttPage` check in `TextBuildJob` — it must be applied before deciding
which `TextBuilder` method to call.
