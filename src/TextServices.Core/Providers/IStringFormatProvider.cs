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
    /// The source format this provider handles, as identified by <see cref="TextFormatDetector"/>.
    /// </summary>
    TextFormat Format { get; }

    /// <summary>
    /// Processes one canvas of text from the raw source string and accumulates the results.
    /// </summary>
    void ProcessPage(
        TextAccumulator accumulator,
        string rawContent,
        string imageIdentifier,
        int canvasWidth,
        int canvasHeight);
}
