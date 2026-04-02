namespace TextServices.Core.Providers;

/// <summary>
/// Processes a single canvas of text from a plain-text source format (e.g. WebVTT)
/// and feeds its content into a <see cref="TextAccumulator"/>.
/// </summary>
public interface ITranscriptFormatProvider
{
    /// <summary>
    /// Returns true if this provider supports the given source format, identified by
    /// the IIIF profile, format MIME type, and/or label string. All may be null.
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
