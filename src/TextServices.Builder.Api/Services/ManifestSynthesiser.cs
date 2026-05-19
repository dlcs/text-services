using IIIF.Presentation;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Annotation;
using IIIF.Presentation.V3.Content;
using IIIF.Serialisation;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Features.Jobs;

namespace TextServices.Builder.Api.Services;

public interface IManifestSynthesiser
{
    /// <summary>
    /// Builds a skeleton IIIF Presentation v3 Manifest from an inline page sequence.
    /// The manifest <c>id</c> is left empty — the text-augmented endpoint patches it at
    /// serve time, exactly as it does for stored real manifests.
    /// </summary>
    string Synthesise(IReadOnlyList<PageInstruction> pages);
}

public class ManifestSynthesiser(TextServicesOptions options) : IManifestSynthesiser
{
    public string Synthesise(IReadOnlyList<PageInstruction> pages)
    {
        var manifest = new Manifest
        {
            Id = string.Empty,
            // pdf-type pages embed an existing PDF — they produce no canvas.
            Items = pages
                .Where(p => !string.Equals(p.Type, "pdf", StringComparison.OrdinalIgnoreCase))
                .Select(BuildCanvas)
                .ToList(),
        };

        manifest.EnsurePresentation3Context();
        return manifest.AsJson();
    }

    private Canvas BuildCanvas(PageInstruction page)
    {
        // Use supplied id, or synthesise a stable placeholder from the input URI.
        // Non-pdf pages should always have an id (validated at submission time).
        var canvasId = !string.IsNullOrEmpty(page.Id) ? page.Id : string.Empty;
        var canvas = new Canvas { Id = canvasId };

        if (page.Width > 0) canvas.Width = page.Width;
        if (page.Height > 0) canvas.Height = page.Height;
        if (page.Duration.HasValue) canvas.Duration = page.Duration.Value;

        // Custom-type pages (non-null type other than "pdf") intentionally carry no
        // painting annotation — they render as generated text pages in the PDF.
        // Normal pages (null type) get a painting annotation when an image URI is available.
        if (page.Type == null)
        {
            // input is accepted as an alias for imageUri on normal image pages.
            var rawImageUri = page.ImageUri ?? page.Input;
            if (rawImageUri != null && ResolveImageUrl(rawImageUri) is { } imageUrl)
            {
                canvas.Items =
                [
                    new AnnotationPage
                    {
                        Id    = $"{canvasId}/painting",
                        Items =
                        [
                            new PaintingAnnotation
                            {
                                Id     = $"{canvasId}/painting/anno",
                                Body   = new Image { Id = imageUrl, Format = "image/jpeg" },
                                Target = new Canvas { Id = canvasId },
                            }
                        ]
                    }
                ];
            }
        }

        return canvas;
    }

    /// <summary>
    /// Resolves an imageUri to a URL suitable for use as a painting annotation body id,
    /// or returns <see langword="null"/> when the painting annotation should be omitted.
    /// <list type="bullet">
    ///   <item>http/https: already a resolved image URL — echoed back unchanged.</item>
    ///   <item>file:// and s3://: cannot be fetched directly by a IIIF viewer.
    ///   When <see cref="TextServicesOptions.AllowFileImageProxy"/> is <c>true</c> and
    ///   <see cref="TextServicesOptions.SearchApiBaseUrl"/> is set, a proxy URL is returned.
    ///   Otherwise <see langword="null"/> is returned and the calling canvas is synthesised
    ///   without a painting annotation — no file path is embedded in the manifest.</item>
    /// </list>
    /// </summary>
    private string? ResolveImageUrl(string imageUri)
    {
        var parsed = new Uri(imageUri);

        if (parsed.Scheme is "http" or "https")
            return imageUri;

        if (options.AllowFileImageProxy && !string.IsNullOrEmpty(options.SearchApiBaseUrl))
        {
            var encoded = Uri.EscapeDataString(imageUri);
            return $"{options.SearchApiBaseUrl.TrimEnd('/')}/proxy/image?uri={encoded}";
        }

        return null;
    }
}
