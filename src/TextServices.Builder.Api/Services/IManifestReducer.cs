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
    /// per canvas. Canvases without an ALTO <c>seeAlso</c> link have <c>Text = null</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the manifest is not IIIF Presentation v3.
    /// </exception>
    IReadOnlyList<PageInstruction> Reduce(string manifestJson);
}
