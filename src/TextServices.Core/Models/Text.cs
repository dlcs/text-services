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

        // Matches the behaviour of both reference implementations:
        //   - Letters and digits are lowercased and kept.
        //   - Whitespace is preserved (collapsed to a single space).
        //   - All other characters (punctuation, symbols) are DROPPED, not replaced with spaces.
        //     e.g. "it's" → "its",  "foo-bar" → "foobar",  "hello, world" → "hello world"
        var sb = new StringBuilder(input.Length);
        bool lastWasSpace = false;

        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(c) && !lastWasSpace && sb.Length > 0)
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
    /// Substring matches are supported and expand to include the whole containing word.
    /// </summary>
    public List<ResultRect> Search(string query)
    {
        var s = Normalise(query);
        if (s.Length == 0 || NormalisedFullText.Length == 0) return [];

        var wordResults = new List<Word>();
        var hitMap = new Dictionary<int, int>(); // Word.Wd → hit number
        int hitCounter = 0;
        int startPos = 0;
        int matchPos;

        while ((matchPos = NormalisedFullText.IndexOf(s, startPos, StringComparison.Ordinal)) != -1)
        {
            // Walk back to the start of the containing word (find the preceding space or start of text).
            // This ensures substring matches capture the whole word, e.g. "ick" → captures "quick".
            int padding = 0;
            while (matchPos > 0 && NormalisedFullText[matchPos - 1] != ' ')
            {
                matchPos--;
                padding++;
            }

            // Step through word positions to collect all words spanning the match.
            // NormalisedFullText has exactly one space between each word, so each step is LenNorm + 1.
            int matchLength = 0;
            int wordPos = matchPos;
            while (matchLength < s.Length + padding)
            {
                var word = Words[wordPos];
                wordResults.Add(word);
                int lengthInText = word.LenNorm + 1; // word length + trailing space
                matchLength += lengthInText;
                wordPos += lengthInText;
                hitMap[word.Wd] = hitCounter;
            }

            hitCounter++;
            startPos = matchPos + matchLength;
            if (startPos >= NormalisedFullText.Length - 1) break;
        }

        return GetRectangles(s, wordResults, hitMap);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Coalesces adjacent same-line words into single ResultRects, then adds context.
    /// For single-word queries the coalescing step is skipped.
    /// </summary>
    private List<ResultRect> GetRectangles(string s, List<Word> words, Dictionary<int, int> hitMap)
    {
        if (words.Count == 0) return [];

        if (!s.Contains(' '))
        {
            // Single-word query: each result is its own hit; use element index as hit number
            // (matches reference behaviour: results.Select(ResultRect.FromWord) with the
            // two-argument Select overload passes (element, index) → FromWord(word, index)).
            var rects = words.Select((w, i) => ResultRect.FromWord(w, i)).ToList();
            AddContext(rects);
            return rects;
        }

        var coalesced = new List<ResultRect>();
        var current = ResultRect.FromWord(words[0], hitMap[words[0].Wd]);

        for (int i = 1; i < words.Count; i++)
        {
            var next = words[i];
            if (current.Li == next.Li && current.Wds[^1] == next.Wd - 1)
            {
                // Adjacent words on the same line — expand the rect.
                // Note: Y/H expansion matches the reference (Math.Min Y, Math.Max H independently).
                current.Y = Math.Min(current.Y, next.Y);
                current.H = Math.Max(current.H, next.H);
                current.W = (next.X + next.W) - current.X;
                current.Wds.Add(next.Wd);
                current.PosNorms.Add(next.PosNorm);
                current.Sp = next.Sp;
                current.ContentNorm += " " + next; // next.ToString() = ContentNorm
                current.ContentRaw += " " + next.ToRawString();
            }
            else
            {
                coalesced.Add(current.ShallowCopy());
                current = ResultRect.FromWord(next, hitMap[next.Wd]);
            }
        }
        coalesced.Add(current);
        AddContext(coalesced);
        return coalesced;
    }

    /// <summary>
    /// Adds Before/After context to each ResultRect using raw character offsets into
    /// <see cref="RawFullText"/> (150 characters either side, matching the reference implementations).
    /// Context is skipped entirely when there are 100 or more results.
    /// </summary>
    private void AddContext(List<ResultRect> results)
    {
        const int maxResultsWithContext = 100;
        const int snippetSize = 150;
        if (results.Count >= maxResultsWithContext) return;

        foreach (var rect in results)
        {
            var words = rect.PosNorms.Select(p => Words[p]).ToList();

            var posRaw = words[0].PosRaw;
            var preOffset = Math.Max(0, posRaw - snippetSize);
            rect.Before = RawFullText.Substring(preOffset, posRaw - preOffset);

            var lastWord = words[^1];
            var postOffset = lastWord.PosRaw + lastWord.LenRaw;
            var postLen = Math.Min(snippetSize, RawFullText.Length - postOffset);
            rect.After = postLen > 0 ? RawFullText.Substring(postOffset, postLen) : string.Empty;
        }
    }
}
