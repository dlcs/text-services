using ProtoBuf;

namespace TextServices.Core.Models;

/// <summary>
/// Marks the boundary of a single image (Canvas) within the document-wide text index.
/// Each Image records where in the full-text its words begin.
/// </summary>
[ProtoContract]
public class Image
{
    /// <summary>
    /// The position in the normalised full-text string where this image's words start.
    /// Corresponds to the key of the first Word entry for this image in <see cref="Text.Words"/>.
    /// </summary>
    [ProtoMember(1)] public int StartCharacter { get; set; }

    /// <summary>
    /// The identifier for this image — typically the IIIF Canvas id,
    /// or whatever identifier the caller supplied for this page in the build sequence.
    /// </summary>
    [ProtoMember(2)] public string ImageIdentifier { get; set; } = string.Empty;
}
