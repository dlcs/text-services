using System.Text;
using System.Xml;
using TextServices.Core.Models;

namespace TextServices.Search.Api.Features.Svg;

/// <summary>
/// Builds an SVG document for a single canvas containing one invisible, selectable
/// <c>&lt;text&gt;</c> element per text line, positioned and stretched to the line's bounding
/// box. Intended to be overlaid on the canvas image so the page text can be selected/copied.
/// </summary>
public static class SvgBuilder
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private const string InvisibleStyle = """

        .text-line-segment { fill: rgba(0,0,0,0) }
        .text-line-segment::selection { fill: #fff; background: rgba(15, 76, 155, 0.8) }

        """;

    private static readonly XmlWriterSettings XmlWriterSettings = new()
    {
        Indent = true,
        OmitXmlDeclaration = true,
    };

    /// <summary>
    /// Builds the SVG document for the canvas at <paramref name="canvasIndex"/>.
    /// </summary>
    /// <param name="text">The loaded text index.</param>
    /// <param name="canvasIndex">Zero-based canvas index (matches <c>Text.Images</c> order).</param>
    /// <param name="width">Canvas width, used for the SVG <c>viewBox</c>.</param>
    /// <param name="height">Canvas height, used for the SVG <c>viewBox</c>.</param>
    /// <returns>The SVG document as a string.</returns>
    public static string Build(Text text, int canvasIndex, int width, int height)
    {
        // Group words by line (Li is a globally unique line counter), in document order.
        var lines = text.Words.Values
            .Where(w => w.Idx == canvasIndex)
            .OrderBy(w => w.Wd)
            .GroupBy(w => w.Li)
            .OrderBy(g => g.Key);

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, XmlWriterSettings))
        {
            writer.WriteStartElement("svg", SvgNamespace);
            writer.WriteAttributeString("viewBox", $"0 0 {width} {height}");

            foreach (var line in lines)
            {
                var lineWords = line.ToList();
                var lineText = string.Join(" ", lineWords.Select(w => w.ContentRaw));
                if (string.IsNullOrWhiteSpace(lineText)) continue;

                var x = lineWords.Min(w => w.X);
                var y = lineWords.Min(w => w.Y);
                var w = lineWords.Max(w => w.X + w.W) - x;
                var h = lineWords.Max(w => w.Y + w.H) - y;
                if (w <= 0 || h <= 0) continue;

                writer.WriteStartElement("text", SvgNamespace);
                writer.WriteAttributeString("x", $"{x}");
                // Baseline is approximately 3/4 of the way down the line box.
                writer.WriteAttributeString("y", $"{(int)(y + h * 0.75)}");
                writer.WriteAttributeString("textLength", $"{w}");
                writer.WriteAttributeString("font-size", $"{h}");
                writer.WriteAttributeString("lengthAdjust", "spacingAndGlyphs");
                writer.WriteAttributeString("class", "text-line-segment");
                writer.WriteString(lineText);
                writer.WriteEndElement();
            }

            writer.WriteStartElement("style", SvgNamespace);
            writer.WriteString(InvisibleStyle);
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        return sb.ToString();
    }
}
