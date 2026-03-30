namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// A single page entry in an inline job submission (<c>sourceData</c>).
/// </summary>
public class PageInstruction
{
    /// <summary>Canvas identifier URI.</summary>
    public required string Id { get; set; }

    /// <summary>Canvas width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Canvas height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>URI of the text file for this page (ALTO, hOCR, etc.). Null for sparse pages.</summary>
    public string? Text { get; set; }

    /// <summary>IIIF seeAlso profile URI, used to select the correct text-format provider.</summary>
    public string? Profile { get; set; }

    /// <summary>IIIF seeAlso label, used as a fallback when Profile is absent or unrecognised.</summary>
    public string? Label { get; set; }
}
