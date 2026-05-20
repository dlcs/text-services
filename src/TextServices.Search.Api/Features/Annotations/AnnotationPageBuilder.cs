using System.Globalization;
using System.Text.Json.Nodes;
using TextServices.Core.Models;

namespace TextServices.Search.Api.Features.Annotations;

/// <summary>
/// Builds a IIIF Presentation 3 <c>AnnotationPage</c> from a <see cref="Text"/> object
/// at either line or word granularity, as defined by the IIIF Text Granularity extension:
/// https://iiif.io/api/extension/text-granularity/
/// </summary>
public static class AnnotationPageBuilder
{
    private const string Pres3Context = "http://iiif.io/api/presentation/3/context.json";
    private const string GranularityContext = "https://iiif.io/api/extension/text-granularity/context.json";

    /// <summary>
    /// Builds an <c>AnnotationPage</c> for the canvas at <paramref name="canvasIndex"/>.
    /// </summary>
    /// <param name="text">The loaded text index.</param>
    /// <param name="canvasIndex">Zero-based canvas index (matches <c>Text.Images</c> order).</param>
    /// <param name="selfUrl">The fully-qualified URL of this annotation page (used as <c>id</c>
    /// and as a prefix for per-annotation <c>id</c> values).</param>
    /// <param name="wordLevel">
    /// <see langword="true"/> for word-level annotations (<c>textGranularity: "word"</c>);
    /// <see langword="false"/> for line-level annotations (<c>textGranularity: "line"</c>).
    /// </param>
    /// <returns>The annotation page as a <see cref="JsonObject"/>.</returns>
    public static JsonObject Build(Text text, int canvasIndex, string selfUrl, bool wordLevel)
    {
        var image = text.Images[canvasIndex];
        var canvasId = image.ImageIdentifier;
        var isTemporal = image.IsTemporalContent;

        // All words for this canvas, in document order.
        var canvasWords = text.Words.Values
            .Where(w => w.Idx == canvasIndex)
            .OrderBy(w => w.Wd)
            .ToList();

        var items = wordLevel
            ? BuildWordItems(canvasWords, canvasId, selfUrl, isTemporal)
            : BuildLineItems(canvasWords, canvasId, selfUrl, isTemporal);

        return new JsonObject
        {
            ["@context"] = new JsonArray(Pres3Context, GranularityContext),
            ["id"] = selfUrl,
            ["type"] = "AnnotationPage",
            ["textGranularity"] = wordLevel ? "word" : "line",
            ["items"] = items,
        };
    }

    // -------------------------------------------------------------------------
    // Word-level
    // -------------------------------------------------------------------------

    private static JsonArray BuildWordItems(
        IReadOnlyList<Word> words, string canvasId, string selfUrl, bool isTemporal)
    {
        var items = new JsonArray();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var target = isTemporal
                ? $"{canvasId}#t={Sec(word.StartMs)},{Sec(word.EndMs)}"
                : $"{canvasId}#xywh={word.X},{word.Y},{word.W},{word.H}";

            items.Add(Annotation($"{selfUrl}/anno/{i}", word.ContentRaw, target));
        }
        return items;
    }

    // -------------------------------------------------------------------------
    // Line-level
    // -------------------------------------------------------------------------

    private static JsonArray BuildLineItems(
        IReadOnlyList<Word> words, string canvasId, string selfUrl, bool isTemporal)
    {
        var items = new JsonArray();
        var annoIndex = 0;

        // Group words by line (Li is a globally unique line counter).
        foreach (var lineGroup in words.GroupBy(w => w.Li).OrderBy(g => g.Key))
        {
            var lineWords = lineGroup.ToList();
            var lineText = string.Join(" ", lineWords.Select(w => w.ContentRaw));

            string target;
            if (isTemporal)
            {
                var startMs = lineWords.Min(w => w.StartMs);
                var endMs = lineWords.Max(w => w.EndMs);
                target = $"{canvasId}#t={Sec(startMs)},{Sec(endMs)}";
            }
            else
            {
                var x = lineWords.Min(w => w.X);
                var y = lineWords.Min(w => w.Y);
                var right = lineWords.Max(w => w.X + w.W);
                var bottom = lineWords.Max(w => w.Y + w.H);
                target = $"{canvasId}#xywh={x},{y},{right - x},{bottom - y}";
            }

            items.Add(Annotation($"{selfUrl}/anno/{annoIndex++}", lineText, target));
        }

        return items;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static JsonObject Annotation(string id, string value, string target) => new()
    {
        ["id"] = id,
        ["type"] = "Annotation",
        ["motivation"] = "supplementing",
        ["body"] = new JsonObject
        {
            ["type"] = "TextualBody",
            ["value"] = value,
            ["format"] = "text/plain",
        },
        ["target"] = target,
    };

    /// <summary>Converts milliseconds to a decimal-seconds string, e.g. 8500 → "8.5".</summary>
    private static string Sec(int ms) =>
        (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
}
