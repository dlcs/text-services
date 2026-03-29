using System.Text;
using ProtoBuf;

namespace TextServices.Core.Models;

/// <summary>
/// The document-wide text search index for a single IIIF Manifest (or equivalent sequence).
/// Serialised to a single Protobuf binary file per document; loaded into memory on demand
/// by the Search API to serve IIIF Search queries.
/// </summary>
/// <remarks>
/// Protobuf field numbers are fixed — do not change them once data is in production.
/// </remarks>
[ProtoContract]
public class Text
{
    /// <summary>
    /// Full text of the document with all words joined by single spaces,
    /// normalised (lowercase, alphanumeric characters only, whitespace collapsed).
    /// This is the string searched by <see cref="Search"/>.
    /// </summary>
    [ProtoMember(1)] public string NormalisedFullText { get; set; } = string.Empty;

    /// <summary>
    /// Full text of the document as extracted from the source, with words joined by single spaces.
    /// Used to provide context snippets (Before/After) in search results.
    /// </summary>
    [ProtoMember(2)] public string RawFullText { get; set; } = string.Empty;

    /// <summary>
    /// All words in the document, keyed by their start position in <see cref="NormalisedFullText"/>.
    /// </summary>
    [ProtoMember(3)] public Dictionary<int, Word> Words { get; set; } = new();

    /// <summary>
    /// One entry per page (Canvas) in the document, recording where each page's words
    /// start in the full-text and the page identifier.
    /// </summary>
    [ProtoMember(4)] public Image[] Images { get; set; } = [];

    /// <summary>
    /// Non-text regions (tables, illustrations, figures) extracted from the source.
    /// </summary>
    [ProtoMember(5)] public ComposedBlock[] ComposedBlocks { get; set; } = [];

    /// <summary>True if no words were found in the source document.</summary>
    public bool IsEmpty => Words.Count == 0;

    // -------------------------------------------------------------------------
    // Normalisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Normalises a string to lowercase alphanumeric characters with single-space separators.
    /// This is the canonical normalisation used throughout the text index.
    /// </summary>
    public static string Normalise(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        bool lastWasSpace = true; // treats leading non-alphanumeric as whitespace

        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        // Trim trailing space added by the loop
        if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
            sb.Length--;

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------

    /// <summary>
    /// Searches for all occurrences of <paramref name="query"/> in this document.
    /// Returns a list of <see cref="ResultRect"/> instances, one per line-segment per hit.
    /// Multiple rects sharing the same <see cref="ResultRect.Hit"/> number belong to the same match.
    /// </summary>
    public List<ResultRect> Search(string query)
    {
        var normQuery = Normalise(query);
        var results = new List<ResultRect>();

        if (normQuery.Length == 0 || NormalisedFullText.Length == 0)
            return results;

        // Sort words once by their normalised text position for efficient per-hit traversal.
        var sortedWords = Words.Values.OrderBy(w => w.PosNorm).ToList();

        int searchFrom = 0;
        int hitNumber = 0;

        while (searchFrom <= NormalisedFullText.Length - normQuery.Length)
        {
            int matchPos = NormalisedFullText.IndexOf(normQuery, searchFrom, StringComparison.Ordinal);
            if (matchPos < 0) break;

            searchFrom = matchPos + 1;
            hitNumber++;
            int matchEnd = matchPos + normQuery.Length;

            // Find all words whose normalised text overlaps the match range.
            var hitWords = sortedWords
                .Where(w => w.PosNorm < matchEnd && w.PosNorm + w.LenNorm > matchPos)
                .ToList();

            if (hitWords.Count == 0) continue;

            var rects = CoalesceWords(hitWords, hitNumber);
            results.AddRange(rects);
        }

        AddContext(results, sortedWords);
        return results;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Coalesces adjacent words (same image, same line, consecutive word numbers)
    /// into single ResultRects. Words that are not adjacent start a new rect.
    /// </summary>
    private static List<ResultRect> CoalesceWords(List<Word> words, int hitNumber)
    {
        var rects = new List<ResultRect>();
        var current = ResultRect.FromWord(words[0], hitNumber);

        for (int i = 1; i < words.Count; i++)
        {
            var word = words[i];

            bool sameImage = word.Idx == current.Idx;
            bool sameLine = word.Li == current.Li;
            bool adjacent = word.Wd == current.Wds[current.Wds.Count - 1] + 1;

            if (sameImage && sameLine && adjacent)
            {
                // Expand bounding box to include this word.
                int newRight = Math.Max(current.X + current.W, word.X + word.W);
                int newBottom = Math.Max(current.Y + current.H, word.Y + word.H);
                current.X = Math.Min(current.X, word.X);
                current.Y = Math.Min(current.Y, word.Y);
                current.W = newRight - current.X;
                current.H = newBottom - current.Y;
                current.Wds.Add(word.Wd);
                current.PosNorms.Add(word.PosNorm);
                current.ContentNorm += " " + word.ContentNorm;
                current.ContentRaw += " " + word.ContentRaw;
            }
            else
            {
                rects.Add(current);
                current = ResultRect.FromWord(word, hitNumber);
            }
        }

        rects.Add(current);
        return rects;
    }

    /// <summary>
    /// Populates <see cref="ResultRect.Before"/> and <see cref="ResultRect.After"/> context
    /// on the first and last rect of each hit respectively, using surrounding words from
    /// <see cref="RawFullText"/>.
    /// </summary>
    private void AddContext(List<ResultRect> results, List<Word> sortedWords)
    {
        const int contextWordCount = 6;

        // Group by hit number; within each hit find the first and last rect.
        var hitGroups = results
            .GroupBy(r => r.Hit)
            .Select(g => g.OrderBy(r => r.Idx).ThenBy(r => r.PosNorm).ToList());

        foreach (var rects in hitGroups)
        {
            var firstRect = rects[0];
            var lastRect = rects[rects.Count - 1];

            int firstPosNorm = firstRect.PosNorms[0];
            int lastPosNorm = lastRect.PosNorms[lastRect.PosNorms.Count - 1];

            int firstIdx = sortedWords.FindIndex(w => w.PosNorm == firstPosNorm);
            int lastIdx = sortedWords.FindIndex(w => w.PosNorm == lastPosNorm);

            if (firstIdx < 0 || lastIdx < 0) continue;

            // Before context: up to contextWordCount words before the hit on the same image.
            if (firstIdx > 0)
            {
                int startIdx = Math.Max(0, firstIdx - contextWordCount);
                // Don't cross image boundaries.
                while (startIdx < firstIdx && sortedWords[startIdx].Idx < firstRect.Idx)
                    startIdx++;

                if (startIdx < firstIdx)
                {
                    var beforeStart = sortedWords[startIdx];
                    var beforeEnd = sortedWords[firstIdx - 1];
                    int rawStart = beforeStart.PosRaw;
                    int rawEnd = beforeEnd.PosRaw + beforeEnd.LenRaw;
                    if (rawEnd <= RawFullText.Length)
                        firstRect.Before = RawFullText[rawStart..rawEnd];
                }
            }

            // After context: up to contextWordCount words after the hit on the same image.
            if (lastIdx < sortedWords.Count - 1)
            {
                int endIdx = Math.Min(sortedWords.Count - 1, lastIdx + contextWordCount);
                // Don't cross image boundaries.
                while (endIdx > lastIdx && sortedWords[endIdx].Idx > lastRect.Idx)
                    endIdx--;

                if (endIdx > lastIdx)
                {
                    var afterStart = sortedWords[lastIdx + 1];
                    var afterEnd = sortedWords[endIdx];
                    int rawStart = afterStart.PosRaw;
                    int rawEnd = afterEnd.PosRaw + afterEnd.LenRaw;
                    if (rawEnd <= RawFullText.Length)
                        lastRect.After = RawFullText[rawStart..rawEnd];
                }
            }
        }
    }
}
