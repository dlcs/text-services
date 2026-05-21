using System.Xml.Linq;
using TextServices.Core.Models;

namespace TextServices.Core.Providers;

/// <summary>
/// Accepts a sequence of pages (each represented as a parsed ALTO or other
/// format root element) and builds a single <see cref="TextBuildResult"/>.
/// </summary>
/// <remarks>
/// Typical usage:
/// <code>
/// var builder = new TextBuilder();
/// foreach (var page in pages)
///     builder.AddPage(page.Id, page.Width, page.Height, page.AltoXml, page.Profile, page.Label);
/// var result = builder.Build();
/// </code>
/// Pages without an associated text document (sparse manifests) should pass
/// <see langword="null"/> for <paramref name="root"/>; they are silently skipped.
/// </remarks>
public class TextBuilder
{
    private readonly IReadOnlyList<ITextFormatProvider> _providers;
    private readonly IReadOnlyList<IStringFormatProvider> _transcriptProviders;
    private readonly TextAccumulator _accumulator = new();

    /// <summary>
    /// Initialises a <see cref="TextBuilder"/> with the default set of providers
    /// (ALTO ns-v2 / ns-v3, hOCR, and VTT).
    /// </summary>
    public TextBuilder() : this(
        [new AltoTextFormatProvider(), new HocrTextFormatProvider()],
        [new VttTextFormatProvider(), new W3cAnnotationTextFormatProvider()])
    { }

    /// <summary>
    /// Initialises a <see cref="TextBuilder"/> with an explicit list of providers,
    /// tried in order until one reports that it <see cref="ITextFormatProvider.Supports"/>
    /// the given profile/label.
    /// </summary>
    public TextBuilder(IReadOnlyList<ITextFormatProvider> providers, IReadOnlyList<IStringFormatProvider>? transcriptProviders = null)
    {
        _providers = providers;
        _transcriptProviders = transcriptProviders ?? [];
    }

    /// <summary>
    /// Adds one page to the build. The correct provider is selected based on
    /// <paramref name="profile"/> and <paramref name="label"/>; if no provider
    /// supports the format the page is silently skipped.
    /// </summary>
    /// <param name="id">Canvas identifier URI for this page.</param>
    /// <param name="canvasWidth">Canvas width in pixels.</param>
    /// <param name="canvasHeight">Canvas height in pixels.</param>
    /// <param name="root">Parsed root element of the text document, or <see langword="null"/> for a sparse page.</param>
    /// <param name="profile">IIIF seeAlso profile URI (used to select the provider).</param>
    /// <param name="label">IIIF seeAlso label (used to select the provider if profile is absent).</param>
    public void AddPage(
        string id,
        int canvasWidth,
        int canvasHeight,
        XElement? root,
        string? profile = null,
        string? label = null)
    {
        if (root == null) return;

        var provider = _providers.FirstOrDefault(p => p.Supports(profile, label));
        if (provider == null) return;

        provider.ProcessPage(_accumulator, root, id, canvasWidth, canvasHeight);
    }

    /// <summary>
    /// Adds one transcript page (VTT or similar plain-text format) to the build.
    /// </summary>
    public void AddTranscriptPage(
        string id,
        int canvasWidth,
        int canvasHeight,
        string? rawContent,
        string? profile = null,
        string? format = null,
        string? label = null)
    {
        if (rawContent == null) return;
        var provider = _transcriptProviders.FirstOrDefault(p => p.Supports(profile, format, label));
        if (provider == null) return;
        provider.ProcessPage(_accumulator, rawContent, id, canvasWidth, canvasHeight);
    }

    /// <summary>
    /// Finalises the build and returns the result.
    /// Returns <see cref="TextBuildResult.Empty"/> if no words were accumulated.
    /// </summary>
    public TextBuildResult Build() => _accumulator.Build();
}
