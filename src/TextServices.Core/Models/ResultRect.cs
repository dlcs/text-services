namespace TextServices.Core.Models;

/// <summary>
/// A bounding rectangle representing one or more coalesced words from a search hit.
/// Not persisted — computed at query time from the <see cref="Text"/> index.
/// Words on the same line within the same hit are merged into a single ResultRect.
/// A multi-line hit will produce multiple ResultRects all sharing the same <see cref="Hit"/> number.
/// </summary>
public class ResultRect
{
    public ResultRect(string contentNorm, string contentRaw)
    {
        ContentNorm = contentNorm;
        ContentRaw = contentRaw;
    }

    /// <summary>Normalised text of the coalesced words in this rect.</summary>
    public string ContentNorm { get; set; }

    /// <summary>Raw (original) text of the coalesced words in this rect.</summary>
    public string ContentRaw { get; set; }

    /// <summary>Bounding box X coordinate in Canvas pixels.</summary>
    public int X { get; set; }

    /// <summary>Bounding box Y coordinate in Canvas pixels.</summary>
    public int Y { get; set; }

    /// <summary>Bounding box width in Canvas pixels.</summary>
    public int W { get; set; }

    /// <summary>Bounding box height in Canvas pixels.</summary>
    public int H { get; set; }

    /// <summary>Start time in milliseconds (temporal results only).</summary>
    public int StartMs { get; set; }
    /// <summary>End time in milliseconds (temporal results only).</summary>
    public int EndMs { get; set; }

    /// <summary>Sequential word numbers of all words in this rect (for adjacency tracking).</summary>
    public List<int> Wds { get; set; } = [];

    /// <summary>Normalised full-text positions of all words in this rect.</summary>
    public List<int> PosNorms { get; set; } = [];

    /// <summary>Line number of the words in this rect.</summary>
    public int Li { get; set; }

    /// <summary>Space width after the last word, in Canvas pixels.</summary>
    public int Sp { get; set; }

    /// <summary>Zero-based image (Canvas) index this rect belongs to.</summary>
    public int Idx { get; set; }

    /// <summary>
    /// Hit number — all ResultRects with the same Hit number belong to the same query match.
    /// A single query match may produce multiple rects if it spans lines.
    /// </summary>
    public int Hit { get; set; }

    /// <summary>Context text immediately preceding this hit (raw characters).</summary>
    public string? Before { get; set; }

    /// <summary>Context text immediately following this hit (raw characters).</summary>
    public string? After { get; set; }

    /// <summary>Start position of this rect's first word in the normalised full-text.</summary>
    public int PosNorm => PosNorms.Count > 0 ? PosNorms[0] : 0;

    /// <summary>Start position of this rect's first word in the raw full-text.</summary>
    public int PosRaw { get; set; }

    /// <summary>Length of ContentNorm.</summary>
    public int LenNorm => ContentNorm.Length;

    /// <summary>Length of ContentRaw.</summary>
    public int LenRaw => ContentRaw.Length;

    public override string ToString() => ContentNorm;

    /// <summary>Creates a ResultRect from a single Word.</summary>
    public static ResultRect FromWord(Word word, int hit) => new(word.ToString(), word.ToRawString())
    {
        X = word.X,
        Y = word.Y,
        W = word.W,
        H = word.H,
        Li = word.Li,
        Sp = word.Sp,
        Idx = word.Idx,
        Hit = hit,
        Wds = [word.Wd],
        PosNorms = [word.PosNorm],
        PosRaw = word.PosRaw,
        StartMs = word.StartMs,
        EndMs = word.EndMs,
    };

    /// <summary>Creates a shallow copy of this ResultRect (used during coalescing).</summary>
    public ResultRect ShallowCopy() => (ResultRect)MemberwiseClone();
}
