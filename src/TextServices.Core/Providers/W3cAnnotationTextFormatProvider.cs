using System.Globalization;
using System.Text.Json;
using TextServices.Core.Models;

namespace TextServices.Core.Providers;

/// <summary>
/// Processes a W3C Web Annotation AnnotationPage JSON document into the text index.
/// Each annotation with a <c>TextualBody</c> body and a spatial (<c>#xywh=</c>) or
/// temporal (<c>#t=</c>) target contributes words to the index.
///
/// <para>Because annotations carry their own bounding boxes, no coordinate rescaling
/// is needed — the xywh values are already in canvas pixel space.</para>
///
/// <para>All words within one annotation share the same bounding box (or time range)
/// and the same line number, so they coalesce correctly in search results.</para>
/// </summary>
public class W3cAnnotationTextFormatProvider : IStringFormatProvider
{
    /// <summary>
    /// Internal sentinel value written into <c>PageInstruction.Format</c> by
    /// <c>ManifestReducer</c> when an externally-referenced AnnotationPage is
    /// detected.  Not a real MIME type — used only for provider dispatch.
    /// </summary>
    public const string FormatSentinel = "iiif-annotation-page";

    public bool Supports(string? profile, string? format, string? label) =>
        format == FormatSentinel;

    public void ProcessPage(
        TextAccumulator accumulator,
        string rawContent,
        string imageIdentifier,
        int canvasWidth,
        int canvasHeight)
    {
        using var doc = JsonDocument.Parse(rawContent);
        var root = doc.RootElement;

        if (!root.TryGetProperty("items", out var items)) return;

        // Collect all (bodyText, target) pairs in one pass so we can determine
        // isTemporalContent before calling BeginPage.
        var entries = new List<(string BodyText, AnnotationTarget Target)>();

        foreach (var anno in items.EnumerateArray())
        {
            if (!HasSupplementingMotivation(anno)) continue;
            if (!anno.TryGetProperty("body", out var bodyEl)) continue;

            var body = bodyEl.ValueKind == JsonValueKind.Array
                ? bodyEl.EnumerateArray().FirstOrDefault()
                : bodyEl;
            if (body.ValueKind != JsonValueKind.Object) continue;

            if (!body.TryGetProperty("type", out var typeEl) ||
                typeEl.GetString() != "TextualBody")
            {
                continue;
            }

            if (!body.TryGetProperty("value", out var valueEl)) continue;
            var text = valueEl.GetString();
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (!anno.TryGetProperty("target", out var targetEl)) continue;
            var target = ParseTarget(targetEl);
            if (target == null) continue;

            entries.Add((text, target.Value));
        }

        if (entries.Count == 0) return;

        // Use the first annotation's target type to set the per-canvas flag.
        // Mixed spatial/temporal annotations on a single canvas are not expected
        // in practice; if they occur the first annotation's type wins.
        var isTemporalContent = entries[0].Target.IsTemporal;
        accumulator.BeginPage(imageIdentifier, isTemporalContent);

        foreach (var (bodyText, target) in entries)
        {
            accumulator.NextLine();

            foreach (var token in bodyText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var norm = Text.Normalise(token);
                if (string.IsNullOrEmpty(norm)) continue;

                if (target.IsTemporal)
                    accumulator.AddWord(token, norm, target.StartMs, target.EndMs);
                else
                    accumulator.AddWord(token, norm, target.X, target.Y, target.W, target.H);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Target parsing
    // -------------------------------------------------------------------------

    private static AnnotationTarget? ParseTarget(JsonElement targetEl) =>
        targetEl.ValueKind switch
        {
            JsonValueKind.String => ParseUriTarget(targetEl.GetString()),
            JsonValueKind.Object => ParseSpecificResource(targetEl),
            _ => null,
        };

    /// <summary>Parses a plain URI target such as <c>canvasId#xywh=10,20,100,50</c>.</summary>
    private static AnnotationTarget? ParseUriTarget(string? uri)
    {
        if (uri == null) return null;
        var hash = uri.IndexOf('#');
        return hash >= 0 ? ParseFragment(uri[(hash + 1)..]) : null;
    }

    /// <summary>
    /// Parses a SpecificResource target:
    /// <code>
    /// { "type": "SpecificResource", "source": "canvasId",
    ///   "selector": { "type": "FragmentSelector", "value": "xywh=10,20,100,50" } }
    /// </code>
    /// <c>selector</c> may also be an array; the first FragmentSelector is used.
    /// </summary>
    private static AnnotationTarget? ParseSpecificResource(JsonElement obj)
    {
        if (!obj.TryGetProperty("type", out var type) ||
            type.GetString() != "SpecificResource")
        {
            return null;
        }

        if (!obj.TryGetProperty("selector", out var selectorEl)) return null;

        // selector can be a single object or an array of selectors.
        JsonElement fragmentSelector;
        if (selectorEl.ValueKind == JsonValueKind.Array)
        {
            var found = false;
            fragmentSelector = default;
            foreach (var s in selectorEl.EnumerateArray())
            {
                if (s.TryGetProperty("type", out var t) && t.GetString() == "FragmentSelector")
                {
                    fragmentSelector = s;
                    found = true;
                    break;
                }
            }
            if (!found) return null;
        }
        else
        {
            fragmentSelector = selectorEl;
            if (!fragmentSelector.TryGetProperty("type", out var t) ||
                t.GetString() != "FragmentSelector")
            {
                return null;
            }
        }

        if (!fragmentSelector.TryGetProperty("value", out var value)) return null;
        return ParseFragment(value.GetString());
    }

    /// <summary>
    /// Parses the fragment portion of a Media Fragments URI.
    /// Accepts <c>xywh=x,y,w,h</c> (with optional <c>pixel:</c> prefix) and
    /// <c>t=start,end</c> (decimal seconds, InvariantCulture).
    /// </summary>
    private static AnnotationTarget? ParseFragment(string? fragment)
    {
        if (fragment == null) return null;

        if (fragment.StartsWith("xywh=", StringComparison.OrdinalIgnoreCase))
        {
            // Strip optional "pixel:" or "percent:" qualifiers (use pixel coords only).
            var coords = fragment[5..];
            if (coords.StartsWith("pixel:", StringComparison.OrdinalIgnoreCase))
                coords = coords[6..];
            else if (coords.StartsWith("percent:", StringComparison.OrdinalIgnoreCase))
                return null; // percentage coordinates not supported

            var parts = coords.Split(',');
            if (parts.Length != 4) return null;
            if (!TryParseInt(parts[0], out var x) ||
                !TryParseInt(parts[1], out var y) ||
                !TryParseInt(parts[2], out var w) ||
                !TryParseInt(parts[3], out var h))
            {
                return null;
            }

            return new AnnotationTarget(false, x, y, w, h, 0, 0);
        }

        if (fragment.StartsWith("t=", StringComparison.OrdinalIgnoreCase))
        {
            var parts = fragment[2..].Split(',');
            if (parts.Length != 2) return null;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
            {
                return null;
            }

            return new AnnotationTarget(true, 0, 0, 0, 0,
                (int)(start * 1000), (int)(end * 1000));
        }

        return null;
    }

    /// <summary>
    /// Parses an integer, accepting floats truncated to int (e.g. "10.5" → 10).
    /// The IIIF Media Fragments spec allows float xywh values in theory.
    /// </summary>
    private static bool TryParseInt(string s, out int result)
    {
        if (int.TryParse(s, out result)) return true;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            result = (int)d;
            return true;
        }
        return false;
    }

    private static bool HasSupplementingMotivation(JsonElement annotation)
    {
        if (!annotation.TryGetProperty("motivation", out var motivation)) return false;
        return motivation.ValueKind switch
        {
            JsonValueKind.String => motivation.GetString()
                ?.Equals("supplementing", StringComparison.OrdinalIgnoreCase) ?? false,
            JsonValueKind.Array => motivation.EnumerateArray()
                .Any(m => m.ValueKind == JsonValueKind.String &&
                          m.GetString()?.Equals("supplementing",
                              StringComparison.OrdinalIgnoreCase) == true),
            _ => false,
        };
    }

    private record struct AnnotationTarget(
        bool IsTemporal,
        int X, int Y, int W, int H,
        int StartMs, int EndMs);
}
