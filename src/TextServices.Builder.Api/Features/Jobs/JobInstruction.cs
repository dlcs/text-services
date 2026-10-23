using System.ComponentModel.DataAnnotations;
using TextServices.Storage;

namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// POST body for <c>POST /textbuilder</c>. One of <see cref="SourceUri"/> or
/// <see cref="SourceData"/> must be provided.
/// </summary>
public class JobInstruction : IValidatableObject
{
    /// <summary>
    /// Job key (e.g. "2/books/my-book"). Used as storage key and API path segment.
    /// May contain '/' characters.
    /// </summary>
    [Required]
    public required string Id { get; set; }

    /// <summary>
    /// URI of a IIIF Presentation v3 Manifest.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Inline page sequence. When <see cref="SourceUri"/> is also supplied, limited to
    /// <c>"pdf"</c> entries inserted among the Manifest's canvases at their
    /// <see cref="PageInstruction.Index"/>.
    /// </summary>
    public List<PageInstruction>? SourceData { get; set; }

    /// <summary>
    /// Bitmask of services/derivatives the job should produce and expose.
    /// Defaults to <see cref="JobServices.All"/> so existing callers are unaffected.
    /// </summary>
    public JobServices Services { get; set; } = JobServices.All;

    /// <summary>
    /// Optional document title used as PDF metadata and Content-Disposition filename.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Named custom page types referenced by <see cref="PageInstruction.Type"/>.
    /// Each entry defines the message rendered centred on the generated PDF page.
    /// </summary>
    public Dictionary<string, CustomPageType>? CustomTypes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Id.StartsWith('/'))
        {
            yield return new ValidationResult(
                "Job ID must not start with '/'.",
                [nameof(Id)]);
        }

        var hasUri = !string.IsNullOrWhiteSpace(SourceUri);
        var hasData = SourceData is { Count: > 0 };

        if (!hasUri && !hasData)
        {
            yield return new ValidationResult(
                "One of sourceUri or sourceData must be provided.",
                [nameof(SourceUri), nameof(SourceData)]);
        }

        if (hasUri && hasData)
        {
            // Alongside a sourceUri, sourceData may only carry indexed pdf entries.
            // Only index 0 (before the first canvas) is currently supported.
            foreach (var page in SourceData!)
            {
                if (!page.IsIndexedPdf() || page.Index != 0)
                {
                    yield return new ValidationResult(
                        "When sourceUri is provided, sourceData may only contain entries with type 'pdf' and index 0.",
                        [nameof(SourceUri), nameof(SourceData)]);
                }
                else if (string.IsNullOrWhiteSpace(page.Input))
                {
                    yield return new ValidationResult(
                        "Each pdf page in sourceData must supply an 'input' (PDF URI).",
                        [nameof(SourceData)]);
                }
            }
        }
        else if (hasData)
        {
            foreach (var page in SourceData!)
            {
                if (!page.IsPdf() && string.IsNullOrEmpty(page.Id))
                {
                    yield return new ValidationResult(
                        "Each non-pdf page in sourceData must supply an 'id' (canvas identifier URI).",
                        [nameof(SourceData)]);
                }
            }

            // index is optional, but once used it determines order, so every entry needs one.
            if (SourceData!.Any(p => p.Index != null) && SourceData!.Any(p => p.Index == null))
            {
                yield return new ValidationResult(
                    "If 'index' is set on any sourceData entry, it must be set on all entries.",
                    [nameof(SourceData)]);
            }
        }

        if (hasData && SourceData!.Where(p => p.Index != null).GroupBy(p => p.Index).Any(g => g.Count() > 1))
        {
            yield return new ValidationResult(
                "Each 'index' in sourceData must be unique.",
                [nameof(SourceData)]);
        }
    }
}
