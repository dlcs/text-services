using ProtoBuf;

namespace TextServices.Core.Models;

/// <summary>
/// Represents a non-text region (table, illustration, figure) found in the source document,
/// as extracted from ALTO &lt;ComposedBlock&gt; elements.
/// These are ultimately used to annotate the Manifest with image/table locations.
/// </summary>
[ProtoContract]
public class ComposedBlock
{
    /// <summary>Zero-based index of the image (Canvas) that contains this block.</summary>
    [ProtoMember(1)] public int ImageIndex { get; set; }

    /// <summary>Position in the normalised full-text where this block starts (word position).</summary>
    [ProtoMember(2)] public int StartCharacter { get; set; }

    /// <summary>Position in the normalised full-text where this block ends (word position).</summary>
    [ProtoMember(3)] public int EndCharacter { get; set; }

    /// <summary>Sequential index of this block within the document.</summary>
    [ProtoMember(4)] public int ComposedBlockIndex { get; set; }

    /// <summary>Bounding box X coordinate in Canvas pixels.</summary>
    [ProtoMember(5)] public int X { get; set; }

    /// <summary>Bounding box Y coordinate in Canvas pixels.</summary>
    [ProtoMember(6)] public int Y { get; set; }

    /// <summary>Bounding box width in Canvas pixels.</summary>
    [ProtoMember(7)] public int W { get; set; }

    /// <summary>Bounding box height in Canvas pixels.</summary>
    [ProtoMember(8)] public int H { get; set; }

    /// <summary>The ALTO block type, e.g. "Table", "Illustration", "Figure".</summary>
    [ProtoMember(9)] public string? BlockType { get; set; }
}
