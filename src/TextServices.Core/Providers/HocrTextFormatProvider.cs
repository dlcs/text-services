using System.Xml.Linq;
using TextServices.Core.Models;

namespace TextServices.Core.Providers;

/// <summary>
/// Processes a single hOCR page and feeds its words into a <see cref="TextAccumulator"/>.
///
/// hOCR is an HTML microformat in which OCR output is encoded using class attributes and
/// a <c>title</c> attribute carrying space-separated key-value pairs, the most important
/// of which is the bounding box: <c>bbox x0 y0 x1 y1</c> (top-left to bottom-right).
///
/// Supported producers include Tesseract OCR (using <c>ocrx_word</c> for words) and any
/// engine producing <c>ocr_word</c> spans.  Well-formed XHTML input is required;
/// <see cref="XElement.Parse"/> is used by the fetcher before this provider receives the root.
/// </summary>
public class HocrTextFormatProvider : ITextFormatProvider
{
    /// <inheritdoc/>
    /// <remarks>
    /// Detects hOCR by looking for "hocr" in the seeAlso <paramref name="profile"/> URI
    /// (e.g. <c>text/vnd.hocr+html</c>) or in the human-readable <paramref name="label"/>.
    /// </remarks>
    public bool Supports(string? profile, string? label)
    {
        if (profile != null && profile.Contains("hocr", StringComparison.OrdinalIgnoreCase))
            return true;
        if (label != null && label.Contains("hocr", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <inheritdoc/>
    public void ProcessPage(
        TextAccumulator accumulator,
        XElement root,
        string imageIdentifier,
        int canvasWidth,
        int canvasHeight)
    {
        // Locate the ocr_page element — may be the root itself or a descendant.
        var pageEl = FindByClass(root, "ocr_page").FirstOrDefault() ?? root;

        // Derive the coordinate scale from the page-level bbox when present.
        // hOCR bbox coords are x0 y0 x1 y1; for a page starting at 0,0 x1=pageW, y1=pageH.
        var pageBbox = ParseBbox(pageEl.Attribute("title")?.Value);
        float pageW = pageBbox.HasValue && pageBbox.Value.W > 0 ? pageBbox.Value.W : canvasWidth;
        float pageH = pageBbox.HasValue && pageBbox.Value.H > 0 ? pageBbox.Value.H : canvasHeight;

        float scaleW = pageW > 0 ? canvasWidth / pageW : 1f;
        float scaleH = pageH > 0 ? canvasHeight / pageH : 1f;

        accumulator.BeginPage(imageIdentifier);

        // ---- Words --------------------------------------------------------------
        // Iterate lines first so we can issue NextLine() for each one.
        // Words are children of lines; try ocrx_word (Tesseract) then ocr_word.
        foreach (var lineEl in FindByClass(pageEl, "ocr_line"))
        {
            accumulator.NextLine();

            // Deduplicate: if both ocrx_word and ocr_word appear (unusual) take ocrx_word.
            var wordEls = FindByClass(lineEl, "ocrx_word").ToList();
            if (wordEls.Count == 0)
                wordEls = FindByClass(lineEl, "ocr_word").ToList();

            foreach (var wordEl in wordEls)
            {
                var bbox = ParseBbox(wordEl.Attribute("title")?.Value);
                if (bbox is null) continue;

                // Collect all text under this element, ignoring child markup.
                var rawWord = string.Concat(
                    wordEl.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim();

                if (string.IsNullOrWhiteSpace(rawWord)) continue;

                var normWord = Text.Normalise(rawWord);
                if (string.IsNullOrEmpty(normWord)) continue;

                accumulator.AddWord(
                    rawWord, normWord,
                    ScaleCoord(bbox.Value.X, scaleW),
                    ScaleCoord(bbox.Value.Y, scaleH),
                    ScaleCoord(bbox.Value.W, scaleW),
                    ScaleCoord(bbox.Value.H, scaleH));
            }
        }

        // ---- Non-text blocks ----------------------------------------------------
        // ocr_figure / ocr_graphic / ocr_linedrawing / ocr_photo → Illustration
        // ocr_table → Table
        // These are peer elements of ocr_carea/ocr_par within ocr_page, not nested in lines.
        var blockClasses = new (string ClassName, string BlockType)[]
        {
            ("ocr_figure",      "Illustration"),
            ("ocr_graphic",     "Illustration"),
            ("ocr_linedrawing", "Illustration"),
            ("ocr_photo",       "Illustration"),
            ("ocr_table",       "Table"),
        };

        foreach (var (className, blockType) in blockClasses)
        {
            foreach (var blockEl in FindByClass(pageEl, className))
            {
                var bbox = ParseBbox(blockEl.Attribute("title")?.Value);
                if (bbox is null) continue;

                int bw = ScaleCoord(bbox.Value.W, scaleW);
                int bh = ScaleCoord(bbox.Value.H, scaleH);
                if (bw <= 0 || bh <= 0) continue;

                var pos = accumulator.LastWordNormPosition;
                accumulator.AddComposedBlock(
                    ScaleCoord(bbox.Value.X, scaleW),
                    ScaleCoord(bbox.Value.Y, scaleH),
                    bw, bh,
                    blockType,
                    pos, pos);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all descendants of <paramref name="root"/> (including root itself)
    /// whose <c>class</c> attribute contains <paramref name="className"/> as a
    /// whole word (space-delimited).  Namespace-agnostic.
    /// </summary>
    private static IEnumerable<XElement> FindByClass(XElement root, string className)
    {
        // Include the root element itself in case it carries the class.
        var candidates = root.Name.LocalName != "html"
            ? Enumerable.Repeat(root, 1).Concat(root.Descendants())
            : root.Descendants();

        return candidates.Where(e =>
        {
            var cls = e.Attribute("class")?.Value;
            if (cls == null) return false;
            // Split on whitespace to avoid partial-name matches.
            return cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                      .Contains(className);
        });
    }

    /// <summary>
    /// Represents a parsed hOCR bounding box with canvas-space X, Y, W, H.
    /// </summary>
    private readonly record struct Bbox(float X, float Y, float W, float H);

    /// <summary>
    /// Parses the <c>bbox x0 y0 x1 y1</c> property from a hOCR <c>title</c> string.
    /// The title may contain multiple semicolon-separated properties; only the one
    /// starting with "bbox" is used.  Returns null when absent or malformed.
    /// </summary>
    private static Bbox? ParseBbox(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        foreach (var prop in title.Split(';'))
        {
            var trimmed = prop.Trim();
            if (!trimmed.StartsWith("bbox ", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return null;

            if (float.TryParse(parts[0], out float x0) &&
                float.TryParse(parts[1], out float y0) &&
                float.TryParse(parts[2], out float x1) &&
                float.TryParse(parts[3], out float y1))
            {
                return new Bbox(x0, y0, x1 - x0, y1 - y0);
            }

            return null; // found bbox property but could not parse it
        }

        return null;
    }

    private static int ScaleCoord(float value, float scale) => (int)(value * scale);
}
