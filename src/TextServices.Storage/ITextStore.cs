using TextServices.Core.Models;

namespace TextServices.Storage;

/// <summary>
/// Abstraction over a storage backend for persisting Text artefacts, AutoComplete
/// data, and the original IIIF Manifest JSON associated with a job.
/// </summary>
/// <remarks>
/// Keys are job IDs (e.g. <c>"2/books/my-book"</c>) and may contain <c>/</c>
/// characters. Implementations must handle these as path segments or object-key
/// prefixes rather than treating the whole string as a flat filename.
/// </remarks>
public interface ITextStore
{
    /// <summary>Persists a <see cref="Text"/> object for the given key.</summary>
    Task SaveText(string key, Text text);

    /// <summary>
    /// Loads the <see cref="Text"/> object for the given key, or <see langword="null"/>
    /// if no artefact exists.
    /// </summary>
    Task<Text?> LoadText(string key);

    /// <summary>Persists an <see cref="AutoComplete"/> object for the given key.</summary>
    Task SaveAutoComplete(string key, AutoComplete autoComplete);

    /// <summary>
    /// Loads the <see cref="AutoComplete"/> object for the given key, or
    /// <see langword="null"/> if no artefact exists.
    /// </summary>
    Task<AutoComplete?> LoadAutoComplete(string key);

    /// <summary>Persists the raw IIIF Manifest JSON string for the given key.</summary>
    Task SaveManifest(string key, string json);

    /// <summary>
    /// Loads the raw IIIF Manifest JSON string for the given key, or
    /// <see langword="null"/> if no artefact exists.
    /// </summary>
    Task<string?> LoadManifest(string key);

    /// <summary>Persists the raw (un-normalised) full text as a plain-text string.</summary>
    Task SaveRawText(string key, string rawText);

    /// <summary>
    /// Loads the raw full text for the given key, or <see langword="null"/> if it has
    /// not been stored (e.g. the job produced no words, or pre-dates this feature).
    /// </summary>
    Task<string?> LoadRawText(string key);

    /// <summary>
    /// Persists the searchable PDF derivative.
    /// The caller is responsible for disposing <paramref name="pdfStream"/> after the call returns.
    /// </summary>
    Task SavePdf(string key, Stream pdfStream);

    /// <summary>
    /// Returns a readable stream over the stored PDF, or <see langword="null"/> if none exists.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    Task<Stream?> LoadPdf(string key);

    /// <summary>Persists the IIIF AnnotationPage JSON for identified figures.</summary>
    Task SaveFigures(string key, string json);

    /// <summary>
    /// Loads the IIIF AnnotationPage JSON for identified figures, or
    /// <see langword="null"/> if no artefact exists (e.g. the manifest had no ALTO
    /// ComposedBlocks with non-zero dimensions).
    /// </summary>
    Task<string?> LoadFigures(string key);

    /// <summary>
    /// Returns <see langword="true"/> if a <see cref="Text"/> artefact exists
    /// for the given key; <see langword="false"/> otherwise.
    /// </summary>
    Task<bool> Exists(string key);
}
