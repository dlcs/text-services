namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// A single page (canvas) entry in a job's page sequence.
/// This is the canonical reduced model: produced by <c>ManifestReducer</c> from a
/// IIIF Manifest and accepted directly as <c>sourceData</c> in the POST body.
/// </summary>
public class PageInstruction
{
    /// <summary>Canvas identifier URI.</summary>
    public required string Id { get; set; }

    /// <summary>Canvas width in pixels. Zero for temporal-only (audio/video) canvases.</summary>
    public int Width { get; set; }

    /// <summary>Canvas height in pixels. Zero for temporal-only (audio/video) canvases.</summary>
    public int Height { get; set; }

    /// <summary>Canvas duration in seconds. Null for image-only canvases.</summary>
    public double? Duration { get; set; }

    /// <summary>URI of the text resource for this page (ALTO, hOCR, VTT, etc.). Null for sparse pages.</summary>
    public string? TextUri { get; set; }

    /// <summary>IIIF seeAlso profile URI, used alongside Format to select the correct text-format provider.</summary>
    public string? Profile { get; set; }

    /// <summary>IIIF format MIME type (e.g. "text/vtt"), used alongside Profile to select the provider.</summary>
    public string? Format { get; set; }

    /// <summary>IIIF seeAlso label, used as a fallback when Profile is absent or unrecognised.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// URI of the source image for this canvas (e.g. the painting annotation body).
    /// Used when generating a synthetic IIIF Manifest and when building a searchable PDF.
    /// May be an HTTP URL, a file:// path, or an S3 URI.
    /// </summary>
    public string? ImageUri { get; set; }
}
