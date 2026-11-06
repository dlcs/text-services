using TextServices.Builder.Api.Features.Jobs;

namespace TextServices.Builder.Api.Services;

/// <summary>
/// Reduces a IIIF Presentation v3 Manifest JSON string to a flat sequence of
/// <see cref="PageInstruction"/> entries (one per canvas), ready for text building.
/// </summary>
public interface IManifestReducer
{
    /// <summary>
    /// Parses <paramref name="manifestJson"/> and returns one <see cref="PageInstruction"/>
    /// per canvas that can hold text. Canvases without a recognised text source have <c>TextUri = null</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the manifest is not IIIF Presentation v3.
    /// </exception>
    ReducedManifest Reduce(string manifestJson);
}

/// <param name="Pages">Pages, in manifest order.</param>
/// <param name="SkippedCanvases">Number of canvases in the manifest that aren't in <paramref name="Pages"/>.</param>
public record ReducedManifest(IReadOnlyList<PageInstruction> Pages, int SkippedCanvases);
