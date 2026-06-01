using System.ComponentModel.DataAnnotations;
using TextServices.Storage;

namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// POST body for <c>POST /textbuilder</c>. Exactly one of
/// <see cref="SourceUri"/> or <see cref="SourceData"/> must be provided.
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
    /// URI of a IIIF Presentation v3 Manifest. Mutually exclusive with
    /// <see cref="SourceData"/>.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Inline page sequence. Mutually exclusive with <see cref="SourceUri"/>.
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
                "Exactly one of sourceUri or sourceData must be provided.",
                [nameof(SourceUri), nameof(SourceData)]);
        }

        if (hasUri && hasData)
        {
            yield return new ValidationResult(
                "Provide sourceUri or sourceData, not both.",
                [nameof(SourceUri), nameof(SourceData)]);
        }

        if (hasData)
        {
            foreach (var page in SourceData!)
            {
                if (!string.Equals(page.Type, "pdf", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(page.Id))
                {
                    yield return new ValidationResult(
                        "Each non-pdf page in sourceData must supply an 'id' (canvas identifier URI).",
                        [nameof(SourceData)]);
                }
            }
        }
    }
}
