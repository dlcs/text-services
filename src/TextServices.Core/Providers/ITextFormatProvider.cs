using System.Xml.Linq;
using TextServices.Core.Models;

namespace TextServices.Core.Providers;

/// <summary>
/// Processes a single page of text from a supported source format (e.g. METS-ALTO, hOCR)
/// and feeds its words into a <see cref="TextAccumulator"/>.
/// </summary>
/// <remarks>
/// Implement this interface to add support for additional text-segmentation formats.
/// The METS-ALTO implementation is <c>AltoTextFormatProvider</c> in this project.
/// </remarks>
public interface ITextFormatProvider
{
    /// <summary>
    /// The source format this provider handles, as identified by <see cref="TextFormatDetector"/>.
    /// </summary>
    TextFormat Format { get; }

    /// <summary>
    /// Processes one page of text from the parsed source document root element,
    /// scaled to the supplied Canvas dimensions, and accumulates the results.
    /// </summary>
    /// <param name="accumulator">Accumulator to receive words and composed blocks.</param>
    /// <param name="root">Root XML element of the source document.</param>
    /// <param name="imageIdentifier">Identifier for this Canvas/page (e.g. Canvas id URI).</param>
    /// <param name="canvasWidth">Canvas width in pixels.</param>
    /// <param name="canvasHeight">Canvas height in pixels.</param>
    void ProcessPage(
        TextAccumulator accumulator,
        XElement root,
        string imageIdentifier,
        int canvasWidth,
        int canvasHeight);
}
