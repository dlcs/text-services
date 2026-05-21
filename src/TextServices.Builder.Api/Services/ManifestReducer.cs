using System.Text.Json;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Core.Providers;

namespace TextServices.Builder.Api.Services;

/// <summary>
/// Parses a IIIF Presentation v3 Manifest using <see cref="System.Text.Json"/>
/// and returns a flat page sequence. No full IIIF object model is needed —
/// the manifest is treated as plain JSON.
/// </summary>
public class ManifestReducer : IManifestReducer
{
    public IReadOnlyList<PageInstruction> Reduce(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var root = doc.RootElement;

        if (!IsV3(root))
        {
            throw new InvalidOperationException(
                "Only IIIF Presentation v3 manifests are supported. " +
                "The manifest must have @context containing 'presentation/3'.");
        }

        if (!root.TryGetProperty("items", out var items))
            return [];

        var pages = new List<PageInstruction>();

        foreach (var canvas in items.EnumerateArray())
        {
            if (!canvas.TryGetProperty("id", out var idEl)) continue;

            var id = idEl.GetString() ?? string.Empty;
            var source = FindTextSource(canvas);

            canvas.TryGetProperty("width", out var w);
            canvas.TryGetProperty("height", out var h);
            bool hasDimensions = w.ValueKind == JsonValueKind.Number && h.ValueKind == JsonValueKind.Number;
            bool hasDuration = canvas.TryGetProperty("duration", out var durationEl) &&
                                 durationEl.ValueKind == JsonValueKind.Number;

            if (!hasDimensions)
            {
                // Include temporal-only canvases (duration but no width/height) when a
                // VTT or annotation-page source is present; both can carry temporal targets.
                var isTemporalSource = source != null &&
                    (IsVttFormat(source.Profile, source.Format, source.Label) ||
                     source.Format == W3cAnnotationTextFormatProvider.FormatSentinel);
                if (!hasDuration || !isTemporalSource)
                    continue;
            }

            pages.Add(new PageInstruction
            {
                Id = id,
                Width = hasDimensions ? w.GetInt32() : 0,
                Height = hasDimensions ? h.GetInt32() : 0,
                Duration = hasDuration ? durationEl.GetDouble() : null,
                TextUri = source?.Uri,
                Profile = source?.Profile,
                Format = source?.Format,
                Label = source?.Label,
                ImageUri = ExtractImageUri(canvas),
            });
        }

        return pages;
    }

    // -------------------------------------------------------------------------
    // v3 detection
    // -------------------------------------------------------------------------

    private static bool IsV3(JsonElement root)
    {
        if (root.TryGetProperty("@context", out var ctx))
        {
            switch (ctx.ValueKind)
            {
                case JsonValueKind.String:
                    return IsV3Context(ctx.GetString());

                case JsonValueKind.Array:
                    foreach (var item in ctx.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && IsV3Context(item.GetString()))
                            return true;
                    }
                    // If the array contained a v2 context and no v3 context, reject.
                    return false;
            }
        }

        // No @context but has type="Manifest" + items array → treat as v3.
        return root.TryGetProperty("type", out var type) &&
               type.GetString() == "Manifest" &&
               root.TryGetProperty("items", out _);
    }

    private static bool IsV3Context(string? ctx) =>
        ctx != null && ctx.Contains("presentation/3", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // Image URI extraction (painting annotation body)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the <c>id</c> of the first painting annotation body on the canvas,
    /// or <see langword="null"/> if the canvas has no painting content.
    /// </summary>
    private static string? ExtractImageUri(JsonElement canvas)
    {
        if (!canvas.TryGetProperty("items", out var annotPages) ||
            annotPages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var annotPage in annotPages.EnumerateArray())
        {
            if (!annotPage.TryGetProperty("items", out var annotations) ||
                annotations.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var annotation in annotations.EnumerateArray())
            {
                if (!HasPaintingMotivation(annotation)) continue;
                if (!annotation.TryGetProperty("body", out var bodyEl)) continue;

                var body = bodyEl.ValueKind == JsonValueKind.Array
                    ? bodyEl.EnumerateArray().FirstOrDefault()
                    : bodyEl;

                if (body.ValueKind != JsonValueKind.Object) continue;

                var uri = body.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (!string.IsNullOrEmpty(uri)) return uri;
            }
        }

        return null;
    }

    private static bool HasPaintingMotivation(JsonElement annotation)
    {
        if (!annotation.TryGetProperty("motivation", out var motivation)) return false;
        return motivation.ValueKind switch
        {
            JsonValueKind.String => motivation.GetString()?.Equals("painting", StringComparison.OrdinalIgnoreCase) ?? false,
            JsonValueKind.Array => motivation.EnumerateArray()
                .Any(m => m.ValueKind == JsonValueKind.String &&
                          m.GetString()?.Equals("painting", StringComparison.OrdinalIgnoreCase) == true),
            _ => false,
        };
    }

    // -------------------------------------------------------------------------
    // Text-source seeAlso detection (ALTO, hOCR, …)
    // -------------------------------------------------------------------------

    private record TextSource(string Uri, string? Profile, string? Format, string? Label);

    private static TextSource? FindTextSource(JsonElement canvas)
    {
        // 1. Try seeAlso first
        if (canvas.TryGetProperty("seeAlso", out var seeAlso))
        {
            var source = seeAlso.ValueKind switch
            {
                JsonValueKind.Array => FindTextSourceInArray(seeAlso),
                JsonValueKind.Object => TryGetTextSource(seeAlso),
                _ => null,
            };
            if (source != null) return source;
        }

        // 2. Fall back to embedded supplementing annotations (e.g. a VTT body link)
        var embedded = FindTextSourceInAnnotations(canvas);
        if (embedded != null) return embedded;

        // 3. Fall back to externally-referenced AnnotationPages (e.g. Wellcome line annotations)
        return FindExternalAnnotationPage(canvas);
    }

    /// <summary>
    /// Returns a <see cref="TextSource"/> for the first externally-referenced
    /// <c>AnnotationPage</c> found in <c>canvas.annotations</c> — i.e. entries that
    /// have an <c>id</c> but no embedded <c>items</c>.  The page will be fetched at
    /// build time and processed by <see cref="W3cAnnotationTextFormatProvider"/>.
    /// </summary>
    private static TextSource? FindExternalAnnotationPage(JsonElement canvas)
    {
        if (!canvas.TryGetProperty("annotations", out var annotationPages))
            return null;

        foreach (var annoPage in annotationPages.EnumerateArray())
        {
            // Skip embedded pages — already handled by FindTextSourceInAnnotations.
            if (annoPage.TryGetProperty("items", out _)) continue;

            if (!annoPage.TryGetProperty("type", out var type) ||
                type.GetString() != "AnnotationPage")
            {
                continue;
            }

            var uri = annoPage.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (uri != null)
                return new TextSource(uri, null, W3cAnnotationTextFormatProvider.FormatSentinel, null);
        }

        return null;
    }

    private static TextSource? FindTextSourceInArray(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            var source = TryGetTextSource(item);
            if (source != null) return source;
        }
        return null;
    }

    private static TextSource? TryGetTextSource(JsonElement item)
    {
        var profile = item.TryGetProperty("profile", out var p) ? p.GetString() : null;
        var format = item.TryGetProperty("format", out var f) ? f.GetString() : null;
        var label = item.TryGetProperty("label", out var l) ? ExtractLabelText(l) : null;

        if (!IsRecognisedTextFormat(profile, format, label)) return null;

        var uri = item.TryGetProperty("id", out var id) ? id.GetString() : null;
        return uri != null ? new TextSource(uri, profile, format, label) : null;
    }

    private static TextSource? FindTextSourceInAnnotations(JsonElement canvas)
    {
        if (!canvas.TryGetProperty("annotations", out var annotationPages))
            return null;

        foreach (var annoPage in annotationPages.EnumerateArray())
        {
            if (!annoPage.TryGetProperty("items", out var items)) continue;

            foreach (var anno in items.EnumerateArray())
            {
                if (!HasSupplementingMotivation(anno)) continue;
                if (!anno.TryGetProperty("body", out var bodyEl)) continue;

                // body may be an array — take first object
                var body = bodyEl.ValueKind == JsonValueKind.Array
                    ? (bodyEl.EnumerateArray().FirstOrDefault())
                    : bodyEl;
                if (body.ValueKind != JsonValueKind.Object) continue;

                var profile = body.TryGetProperty("profile", out var p) ? p.GetString() : null;
                var format = body.TryGetProperty("format", out var f) ? f.GetString() : null;
                var label = body.TryGetProperty("label", out var l) ? ExtractLabelText(l) : null;

                if (!IsRecognisedTextFormat(profile, format, label)) continue;

                var uri = body.TryGetProperty("id", out var id) ? id.GetString() : null;
                if (uri != null) return new TextSource(uri, profile, format, label);
            }
        }
        return null;
    }

    private static bool HasSupplementingMotivation(JsonElement annotation)
    {
        if (!annotation.TryGetProperty("motivation", out var motivation)) return false;
        return motivation.ValueKind switch
        {
            JsonValueKind.String => motivation.GetString()?.Equals("supplementing", StringComparison.OrdinalIgnoreCase) ?? false,
            JsonValueKind.Array => motivation.EnumerateArray()
                .Any(m => m.ValueKind == JsonValueKind.String &&
                          m.GetString()?.Equals("supplementing", StringComparison.OrdinalIgnoreCase) == true),
            _ => false,
        };
    }

    private static bool IsVttFormat(string? profile, string? format, string? label) =>
        ContainsIgnoreCase(profile, "text/vtt") || ContainsIgnoreCase(format, "text/vtt") ||
        ContainsIgnoreCase(profile, "vtt") || ContainsIgnoreCase(format, "vtt") ||
        ContainsIgnoreCase(label, "vtt") || ContainsIgnoreCase(label, "webvtt") ||
        ContainsIgnoreCase(label, "transcript");

    /// <summary>
    /// Returns <see langword="true"/> if the profile URI, format MIME type, or label indicates
    /// a recognised text format (ALTO, hOCR, or VTT). Detection is intentionally broad.
    /// </summary>
    private static bool IsRecognisedTextFormat(string? profile, string? format, string? label) =>
        ContainsIgnoreCase(profile, "alto") || ContainsIgnoreCase(format, "alto") || ContainsIgnoreCase(label, "alto") ||
        ContainsIgnoreCase(profile, "hocr") || ContainsIgnoreCase(format, "hocr") || ContainsIgnoreCase(label, "hocr") ||
        IsVttFormat(profile, format, label);

    private static bool ContainsIgnoreCase(string? value, string term) =>
        value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts a plain string from a IIIF language map
    /// (<c>{"none": ["text"], "en": ["text"]}</c>) or returns the string directly.
    /// Returns the first value found, regardless of language key.
    /// </summary>
    private static string? ExtractLabelText(JsonElement label)
    {
        if (label.ValueKind == JsonValueKind.String)
            return label.GetString();

        if (label.ValueKind == JsonValueKind.Object)
        {
            foreach (var lang in label.EnumerateObject())
            {
                if (lang.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var val in lang.Value.EnumerateArray())
                    {
                        if (val.ValueKind == JsonValueKind.String)
                            return val.GetString();
                    }
                }
                else if (lang.Value.ValueKind == JsonValueKind.String)
                {
                    return lang.Value.GetString();
                }
            }
        }

        return null;
    }
}
