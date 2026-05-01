namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// A single page entry in a job's page sequence.
/// This is the canonical reduced model: produced by <c>ManifestReducer</c> from a
/// IIIF Manifest and accepted directly as <c>sourceData</c> in the POST body.
/// Aligns with the Fireball PDF-assembly payload format.
/// </summary>
public class PageInstruction
{
    /// <summary>
    /// Page type. Absent or null = normal image/temporal canvas (existing behaviour).
    /// <c>"pdf"</c> = embed an existing PDF at this position; no canvas is generated
    /// in the synthesised Manifest.
    /// Any other non-null value = a custom page type (e.g. <c>"redacted"</c>); the name
    /// must match a key in <see cref="JobInstruction.CustomTypes"/>. A canvas is
    /// generated (no painting annotation, no text), and the PDF renders a centred message.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Canvas identifier URI. Required for all page types except <c>"pdf"</c>.
    /// </summary>
    public string? Id { get; set; }

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
    /// May be an HTTP URL, a <c>file://</c> path, or an S3 URI.
    /// For image pages, <see cref="Input"/> is treated as an alias when this is absent.
    /// </summary>
    public string? ImageUri { get; set; }

    /// <summary>
    /// Source URI for <c>"pdf"</c> type pages: the PDF to embed at this position.
    /// For normal image pages, treated as an alias for <see cref="ImageUri"/> when that
    /// field is absent — enabling direct use of Fireball-style payloads.
    /// Supports <c>http/https</c>, <c>file://</c>, and <c>s3://</c> schemes.
    /// </summary>
    public string? Input { get; set; }
}
