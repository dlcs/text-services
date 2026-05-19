using System.Text;
using TextServices.Core.Models;

namespace TextServices.Core.Providers;

/// <summary>
/// Accumulates words and composed blocks across multiple pages as they are fed in
/// by an <see cref="ITextFormatProvider"/>. Call <see cref="Build"/> when all pages
/// have been processed to obtain the final <see cref="TextBuildResult"/>.
/// </summary>
public class TextAccumulator
{
    private readonly Dictionary<int, Word> _words = new();
    private readonly List<Image> _images = new();
    private readonly List<ComposedBlock> _composedBlocks = new();
    private readonly StringBuilder _normText = new();
    private readonly StringBuilder _rawText = new();

    private int _wordCounter;
    private int _lineCounter;
    private int _composedBlockCounter;
    private bool _hasContent;
    private int _lastWordNormPosition = -1;

    /// <summary>Zero-based index of the current image being accumulated.</summary>
    public int CurrentImageIndex => _images.Count - 1;

    /// <summary>
    /// The normalised-text position of the most recently added word,
    /// or -1 if no words have been added yet. Used by format providers
    /// to track composed-block boundaries.
    /// </summary>
    public int LastWordNormPosition => _lastWordNormPosition;

    /// <summary>
    /// Signals the start of a new page. Must be called before adding any words for that page.
    /// </summary>
    public void BeginPage(string imageIdentifier, bool isTemporalContent = false)
    {
        _images.Add(new Image
        {
            StartCharacter = _normText.Length,
            ImageIdentifier = imageIdentifier,
            IsTemporalContent = isTemporalContent,
        });
        // Do NOT reset _lineCounter here. Li must be a globally unique line number
        // across the entire document (matching both reference implementations).
        // Resetting per page would allow words on different pages to share the same
        // Li value, breaking the coalescing adjacency check in Text.GetRectangles.
    }

    /// <summary>
    /// Increments the line counter. Call at the start of each new text line.
    /// </summary>
    public void NextLine() => _lineCounter++;

    /// <summary>
    /// Adds a word to the accumulator.
    /// </summary>
    /// <param name="contentRaw">Word text as extracted from the source.</param>
    /// <param name="contentNorm">Normalised word text.</param>
    /// <param name="x">Bounding box X in Canvas pixels.</param>
    /// <param name="y">Bounding box Y in Canvas pixels.</param>
    /// <param name="w">Bounding box width in Canvas pixels.</param>
    /// <param name="h">Bounding box height in Canvas pixels.</param>
    /// <param name="spaceAfter">Width of the space after this word in Canvas pixels.</param>
    public void AddWord(
        string contentRaw,
        string contentNorm,
        int x, int y, int w, int h,
        int spaceAfter = 0)
    {
        if (string.IsNullOrEmpty(contentNorm)) return;

        if (_normText.Length > 0)
        {
            _normText.Append(' ');
            _rawText.Append(' ');
        }

        var word = new Word
        {
            ContentRaw = contentRaw,
            ContentNorm = contentNorm,
            X = x,
            Y = y,
            W = w,
            H = h,
            Sp = spaceAfter,
            Wd = _wordCounter++,
            Li = _lineCounter,
            Idx = _images.Count - 1,
            PosNorm = _normText.Length,
            PosRaw = _rawText.Length,
        };

        _words[word.PosNorm] = word;
        _lastWordNormPosition = word.PosNorm;
        _normText.Append(contentNorm);
        _rawText.Append(contentRaw);
        _hasContent = true;
    }

    /// <summary>
    /// Adds a temporal word (from VTT or similar time-coded source) to the accumulator.
    /// </summary>
    public void AddWord(string contentRaw, string contentNorm, int startMs, int endMs)
    {
        if (string.IsNullOrEmpty(contentNorm)) return;

        if (_normText.Length > 0)
        {
            _normText.Append(' ');
            _rawText.Append(' ');
        }

        var word = new Word
        {
            ContentRaw = contentRaw,
            ContentNorm = contentNorm,
            X = 0,
            Y = 0,
            W = 0,
            H = 0,
            Sp = 0,
            StartMs = startMs,
            EndMs = endMs,
            Wd = _wordCounter++,
            Li = _lineCounter,
            Idx = _images.Count - 1,
            PosNorm = _normText.Length,
            PosRaw = _rawText.Length,
        };

        _words[word.PosNorm] = word;
        _lastWordNormPosition = word.PosNorm;
        _normText.Append(contentNorm);
        _rawText.Append(contentRaw);
        _hasContent = true;
    }

    /// <summary>
    /// Adds a non-text composed block (table, illustration, figure) with explicit
    /// start and end character positions in the normalised full text. Both values
    /// are the <see cref="Word.PosNorm"/> of the first and last word in the block.
    /// </summary>
    public void AddComposedBlock(int x, int y, int w, int h, string? blockType,
        int startCharacter, int endCharacter)
    {
        _composedBlocks.Add(new ComposedBlock
        {
            ImageIndex = _images.Count - 1,
            StartCharacter = startCharacter,
            EndCharacter = endCharacter,
            ComposedBlockIndex = _composedBlockCounter++,
            X = x,
            Y = y,
            W = w,
            H = h,
            BlockType = blockType,
        });
        _hasContent = true;
    }

    /// <summary>
    /// Builds and returns the final <see cref="TextBuildResult"/>.
    /// Returns <see cref="TextBuildResult.Empty"/> if no content was accumulated.
    /// </summary>
    public TextBuildResult Build()
    {
        if (!_hasContent) return TextBuildResult.Empty;

        var text = new Text
        {
            NormalisedFullText = _normText.ToString(),
            RawFullText = _rawText.ToString(),
            Words = _words,
            Images = [.. _images],
            ComposedBlocks = [.. _composedBlocks],
        };

        var autoComplete = BuildAutoComplete(text);

        return new TextBuildResult { Text = text, AutoComplete = autoComplete };
    }

    private static AutoComplete BuildAutoComplete(Text text)
    {
        var buckets = new Dictionary<string, HashSet<string>>();

        foreach (var word in text.Words.Values)
        {
            var norm = word.ContentNorm;
            if (norm.Length < 3) continue;

            var prefix = norm[..3];
            if (!buckets.TryGetValue(prefix, out var set))
            {
                set = new HashSet<string>();
                buckets[prefix] = set;
            }
            set.Add(norm);
        }

        return new AutoComplete { Buckets = buckets };
    }
}
