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

    /// <summary>URI of the ALTO (or other format) text file for this page. Null for sparse pages.</summary>
    public string? Text { get; set; }
}
