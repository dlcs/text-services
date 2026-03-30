using System.Runtime.CompilerServices;
using System.Xml.Linq;
using TextServices.Core.Models;

namespace TextServices.Core.Providers;

/// <summary>
/// Processes a single METS-ALTO page and feeds its words into a <see cref="TextAccumulator"/>.
/// Supports both ns-v2 (<c>http://www.loc.gov/standards/alto/ns-v2#</c>) and ns-v3
/// (<c>http://www.loc.gov/standards/alto/ns-v3#</c>) as well as namespace-free ALTO.
/// </summary>
public class AltoTextFormatProvider : ITextFormatProvider
{
    // Hyphen character used in some ALTO sources as a soft-hyphen marker
    private const char HyphenSpecial = '¬';

    /// <inheritdoc/>
    public bool Supports(string? profile, string? label)
    {
        if (profile != null && profile.Contains("alto", StringComparison.OrdinalIgnoreCase))
            return true;
        if (label != null && label.Contains("alto", StringComparison.OrdinalIgnoreCase))
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
        var ns = DetectAltoNamespace(root);

        var pageElement = FindDescendant(root, ns, "Page");
        if (pageElement == null) return;

        float altoWidth  = ParseFloat(pageElement, "WIDTH",  canvasWidth);
        float altoHeight = ParseFloat(pageElement, "HEIGHT", canvasHeight);

        float scaleW = altoWidth  > 0 ? canvasWidth  / altoWidth  : 1f;
        float scaleH = altoHeight > 0 ? canvasHeight / altoHeight : 1f;

        var printSpace = FindDescendant(pageElement, ns, "PrintSpace");
        if (printSpace == null) return;

        accumulator.BeginPage(imageIdentifier);

        // Tracks composed blocks encountered while iterating words.
        // Key: ALTO ID attribute of the ComposedBlock element (or a fallback hash).
        // Value: (first-word PosNorm, last-word PosNorm, the element itself).
        var composedBlockTracker =
            new Dictionary<string, (int Start, int End, XElement Elem)>();

        // A pending HypPart1 word: held locally until the matching HypPart2 arrives.
        var hyphenPending = false;
        string pendingRaw = string.Empty, pendingNorm = string.Empty;
        int pendingX = 0, pendingY = 0, pendingW = 0, pendingH = 0, pendingSp = 0;

        foreach (var textBlock in FindDescendants(printSpace, ns, "TextBlock"))
        {
            foreach (var textLine in FindDescendants(textBlock, ns, "TextLine"))
            {
                accumulator.NextLine();

                foreach (var xString in FindDescendants(textLine, ns, "String"))
                {
                    var rawWord = xString.Attribute("CONTENT")?.Value ?? string.Empty;

                    bool wordIsHyphenFirstPart =
                        xString.Attribute("SUBS_TYPE")?.Value == "HypPart1";

                    if (rawWord.EndsWith(HyphenSpecial))
                    {
                        wordIsHyphenFirstPart = true;
                        rawWord = rawWord[..^1];
                    }

                    int x  = Scale(xString, "HPOS",   scaleW);
                    int y  = Scale(xString, "VPOS",   scaleH);
                    int w  = Scale(xString, "WIDTH",  scaleW);
                    int h  = Scale(xString, "HEIGHT", scaleH);
                    int sp = SpaceAfter(xString, ns, scaleW);

                    if (hyphenPending)
                    {
                        // Complete the hyphenated word at the first-part's position.
                        var subsContent = xString.Attribute("SUBS_CONTENT")?.Value;
                        var combinedRaw  = subsContent ?? (pendingRaw + rawWord);
                        var combinedNorm = Text.Normalise(combinedRaw);

                        accumulator.AddWord(combinedRaw, combinedNorm,
                            pendingX, pendingY, pendingW, pendingH, pendingSp);

                        if (!string.IsNullOrEmpty(combinedNorm))
                            TrackComposedBlock(textBlock, ns,
                                accumulator.LastWordNormPosition, composedBlockTracker);

                        hyphenPending = false;
                        wordIsHyphenFirstPart = false;
                        continue;
                    }

                    if (wordIsHyphenFirstPart)
                    {
                        // Hold this word — don't commit to accumulator until HypPart2.
                        (pendingRaw, pendingNorm, pendingX, pendingY, pendingW, pendingH, pendingSp)
                            = (rawWord, Text.Normalise(rawWord), x, y, w, h, sp);
                        hyphenPending = true;
                        continue;
                    }

                    // Ordinary word.
                    var norm = Text.Normalise(rawWord);
                    if (!string.IsNullOrEmpty(norm))
                    {
                        accumulator.AddWord(rawWord, norm, x, y, w, h, sp);
                        TrackComposedBlock(textBlock, ns,
                            accumulator.LastWordNormPosition, composedBlockTracker);
                    }
                }
            }
        }

        // If the document ends with an unmatched HypPart1 (edge case), commit it as-is.
        if (hyphenPending && !string.IsNullOrEmpty(pendingNorm))
        {
            accumulator.AddWord(pendingRaw, pendingNorm,
                pendingX, pendingY, pendingW, pendingH, pendingSp);
        }

        // Flush composed blocks discovered during word iteration.
        foreach (var (_, info) in composedBlockTracker)
        {
            accumulator.AddComposedBlock(
                Scale(info.Elem, "HPOS",   scaleW),
                Scale(info.Elem, "VPOS",   scaleH),
                Scale(info.Elem, "WIDTH",  scaleW),
                Scale(info.Elem, "HEIGHT", scaleH),
                info.Elem.Attribute("TYPE")?.Value,
                info.Start,
                info.End);
        }

        // Second pass: capture non-text ComposedBlocks (Illustration, Figure, etc.)
        // that contain no TextBlock descendants and were therefore missed by the word
        // iteration above (e.g. purely graphical regions in Wellcome ALTO).
        var seenBlockIds = new HashSet<string>(composedBlockTracker.Keys);
        foreach (var cb in FindDescendants(printSpace, ns, "ComposedBlock"))
        {
            var cbId = cb.Attribute("ID")?.Value
                ?? RuntimeHelpers.GetHashCode(cb).ToString();
            if (!seenBlockIds.Add(cbId)) continue; // already captured via text tracking

            int cbW = Scale(cb, "WIDTH",  scaleW);
            int cbH = Scale(cb, "HEIGHT", scaleH);
            if (cbW <= 0 || cbH <= 0) continue;

            var pos = accumulator.LastWordNormPosition;
            accumulator.AddComposedBlock(
                Scale(cb, "HPOS",   scaleW),
                Scale(cb, "VPOS",   scaleH),
                cbW,
                cbH,
                cb.Attribute("TYPE")?.Value,
                pos,
                pos);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Determines the XML namespace in use by the ALTO document.
    /// Checks the root element's own namespace first; falls back to scanning
    /// for a TextLine element to handle unusual namespace declarations.
    /// </summary>
    private static XNamespace DetectAltoNamespace(XElement root)
    {
        if (root.Name.Namespace != XNamespace.None)
            return root.Name.Namespace;

        // Namespace declared on a child — find any recognisable element
        var sample = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "TextLine");
        return sample?.Name.Namespace ?? XNamespace.None;
    }

    private static XElement? FindDescendant(XElement parent, XNamespace ns, string localName)
    {
        return ns == XNamespace.None
            ? parent.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)
            : parent.Descendants(ns + localName).FirstOrDefault();
    }

    private static IEnumerable<XElement> FindDescendants(XElement parent, XNamespace ns, string localName)
    {
        return ns == XNamespace.None
            ? parent.Descendants().Where(e => e.Name.LocalName == localName)
            : parent.Descendants(ns + localName);
    }

    private static int Scale(XElement element, string attribute, float scale)
    {
        var raw = element.Attribute(attribute)?.Value;
        if (string.IsNullOrEmpty(raw) || !float.TryParse(raw, out float value))
            return 0;
        return (int)(value * scale);
    }

    private static float ParseFloat(XElement element, string attribute, float fallback)
    {
        var raw = element.Attribute(attribute)?.Value;
        return float.TryParse(raw, out float value) && value > 0 ? value : fallback;
    }

    /// <summary>
    /// Returns the width of the SP (space) element that immediately follows
    /// the given String element on the same TextLine, scaled to canvas units.
    /// Returns 0 if no SP element is present.
    /// </summary>
    private static int SpaceAfter(XElement xString, XNamespace ns, float scaleW)
    {
        var spName = ns == XNamespace.None ? XName.Get("SP") : ns + "SP";
        var sp = xString.ElementsAfterSelf(spName).FirstOrDefault();
        return sp != null ? Scale(sp, "WIDTH", scaleW) : 0;
    }

    /// <summary>
    /// Updates the composed-block tracking dictionary after a word has been
    /// added to the accumulator. Checks whether <paramref name="textBlock"/>
    /// is a descendant of a ComposedBlock element and, if so, records the
    /// word's position as the current end of that block (or as the start if
    /// this is the first word in the block).
    /// </summary>
    private static void TrackComposedBlock(
        XElement textBlock,
        XNamespace ns,
        int wordPosNorm,
        Dictionary<string, (int Start, int End, XElement Elem)> tracker)
    {
        var cb = textBlock
            .Ancestors()
            .FirstOrDefault(a => a.Name.LocalName == "ComposedBlock");

        if (cb == null) return;

        var cbId = cb.Attribute("ID")?.Value
            ?? RuntimeHelpers.GetHashCode(cb).ToString();

        if (tracker.TryGetValue(cbId, out var existing))
            tracker[cbId] = existing with { End = wordPosNorm };
        else
            tracker[cbId] = (wordPosNorm, wordPosNorm, cb);
    }
}
