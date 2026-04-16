namespace TextServices.Core.Providers;

/// <summary>
/// Processes a single canvas of text from a plain-string source (e.g. WebVTT,
/// a W3C AnnotationPage JSON document) and feeds its content into a
/// <see cref="TextAccumulator"/>.  Parallel to <see cref="ITextFormatProvider"/>
/// which takes a pre-parsed <see cref="System.Xml.Linq.XElement"/>.
/// </summary>
public interface IStringFormatProvider
{
    /// <summary>
    /// Returns true if this provider handles the given source format, identified by
    /// the IIIF profile URI, format MIME type, and/or label string. All may be null.
    /// </summary>
    bool Supports(string? profile, string? format, string? label);

    /// <summary>
    /// Processes one canvas of text from the raw source string and accumulates the results.
    /// </summary>
    void ProcessPage(
        TextAccumulator accumulator,
        string          rawContent,
        string          imageIdentifier,
        int             canvasWidth,
        int             canvasHeight);
}
