using System.Text.Json;
using TextServices.Builder.Api.Features.Jobs;

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
            throw new InvalidOperationException(
                "Only IIIF Presentation v3 manifests are supported. " +
                "The manifest must have @context containing 'presentation/3'.");

        if (!root.TryGetProperty("items", out var items))
            return [];

        var pages = new List<PageInstruction>();

        foreach (var canvas in items.EnumerateArray())
        {
            if (!canvas.TryGetProperty("id", out var idEl)) continue;

            // Skip canvases that have no spatial dimensions (e.g., audio-only canvases
            // that carry only a duration). Canvases with width, height AND duration
            // (e.g., video) are included — they can still carry ALTO text.
            if (!canvas.TryGetProperty("width",  out var w) ||
                !canvas.TryGetProperty("height", out var h))
                continue;

            var id     = idEl.GetString() ?? string.Empty;
            var source = FindTextSource(canvas);

            pages.Add(new PageInstruction
            {
                Id      = id,
                Width   = w.GetInt32(),
                Height  = h.GetInt32(),
                Text    = source?.Uri,
                Profile = source?.Profile,
                Label   = source?.Label,
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
                        if (item.ValueKind == JsonValueKind.String && IsV3Context(item.GetString()))
                            return true;
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
    // Text-source seeAlso detection (ALTO, hOCR, …)
    // -------------------------------------------------------------------------

    private record TextSource(string Uri, string? Profile, string? Label);

    private static TextSource? FindTextSource(JsonElement canvas)
    {
        if (!canvas.TryGetProperty("seeAlso", out var seeAlso))
            return null;

        return seeAlso.ValueKind switch
        {
            JsonValueKind.Array  => FindTextSourceInArray(seeAlso),
            JsonValueKind.Object => TryGetTextSource(seeAlso),
            _                    => null,
        };
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
        var label   = item.TryGetProperty("label",   out var l) ? ExtractLabelText(l) : null;

        if (!IsRecognisedTextFormat(profile, label)) return null;

        var uri = item.TryGetProperty("id", out var id) ? id.GetString() : null;
        return uri != null ? new TextSource(uri, profile, label) : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the profile URI or label indicates a recognised
    /// text format (ALTO or hOCR).  Detection is intentionally broad.
    /// </summary>
    private static bool IsRecognisedTextFormat(string? profile, string? label) =>
        ContainsIgnoreCase(profile, "alto")  || ContainsIgnoreCase(label, "alto") ||
        ContainsIgnoreCase(profile, "hocr")  || ContainsIgnoreCase(label, "hocr");

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
                        if (val.ValueKind == JsonValueKind.String)
                            return val.GetString();
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
