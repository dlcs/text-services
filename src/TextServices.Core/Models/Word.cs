using ProtoBuf;

namespace TextServices.Core.Models;

/// <summary>
/// A single word in the text, with its bounding box in Canvas coordinates
/// and its positions in both normalised and raw full-text strings.
/// </summary>
/// <remarks>
/// Protobuf field numbers are fixed — do not change them once data is in production.
/// </remarks>
[ProtoContract]
public class Word
{
    /// <summary>Original word text as extracted from the source.</summary>
    [ProtoMember(1)] public string ContentRaw { get; set; } = string.Empty;

    /// <summary>Normalised word text (lowercase, alphanumeric only).</summary>
    [ProtoMember(2)] public string ContentNorm { get; set; } = string.Empty;

    /// <summary>Bounding box X coordinate in Canvas pixels.</summary>
    [ProtoMember(3)] public int X { get; set; }

    /// <summary>Bounding box Y coordinate in Canvas pixels.</summary>
    [ProtoMember(4)] public int Y { get; set; }

    /// <summary>Bounding box width in Canvas pixels.</summary>
    [ProtoMember(5)] public int W { get; set; }

    /// <summary>Bounding box height in Canvas pixels.</summary>
    [ProtoMember(6)] public int H { get; set; }

    /// <summary>Sequential word number across the whole document (used for adjacency checks).</summary>
    [ProtoMember(7)] public int Wd { get; set; }

    /// <summary>Line number within the source page (used for coalescing words on the same line).</summary>
    [ProtoMember(8)] public int Li { get; set; }

    /// <summary>Width of the space after this word, in Canvas pixels (0 if last word on line).</summary>
    [ProtoMember(9)] public int Sp { get; set; }

    /// <summary>Zero-based index of the image (Canvas) this word belongs to.</summary>
    [ProtoMember(10)] public int Idx { get; set; }

    /// <summary>Start position of this word in the normalised full-text string.</summary>
    [ProtoMember(11)] public int PosNorm { get; set; }

    /// <summary>Start position of this word in the raw full-text string.</summary>
    [ProtoMember(12)] public int PosRaw { get; set; }

    /// <summary>Start time in milliseconds (temporal content only; 0 for spatial words).</summary>
    [ProtoMember(13)] public int StartMs { get; set; }
    /// <summary>End time in milliseconds (temporal content only; 0 for spatial words).</summary>
    [ProtoMember(14)] public int EndMs { get; set; }

    /// <summary>Length of this word in the normalised full-text string.</summary>
    public int LenNorm => ContentNorm.Length;

    /// <summary>Length of this word in the raw full-text string.</summary>
    public int LenRaw => ContentRaw.Length;

    /// <summary>Returns the normalised content of this word (used in string concatenation during coalescing).</summary>
    public override string ToString() => ContentNorm;

    /// <summary>Returns the raw content of this word.</summary>
    public string ToRawString() => ContentRaw;
}
