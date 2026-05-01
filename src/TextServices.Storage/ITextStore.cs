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
    /// Persists the manifest-level line-annotation <c>AnnotationPage</c> JSON
    /// (all canvases, line granularity) for the given key.
    /// </summary>
    Task SaveAnnotations(string key, string json);

    /// <summary>
    /// Loads the manifest-level line-annotation <c>AnnotationPage</c> JSON, or
    /// <see langword="null"/> if it has not been stored.
    /// </summary>
    Task<string?> LoadAnnotations(string key);

    /// <summary>
    /// Returns <see langword="true"/> if a <see cref="Text"/> artefact exists
    /// for the given key; <see langword="false"/> otherwise.
    /// </summary>
    Task<bool> Exists(string key);

    /// <summary>
    /// Persists the <see cref="JobServices"/> capability flags for the given key as a
    /// plain integer. Only written when the flags differ from the default (all services
    /// enabled); callers that read <see langword="null"/> should treat all services as enabled.
    /// </summary>
    Task SaveCapabilities(string key, int services);

    /// <summary>
    /// Loads the capability flags for the given key, or <see langword="null"/> if no
    /// capabilities file exists (caller should assume all services are enabled).
    /// </summary>
    Task<int?> LoadCapabilities(string key);

    /// <summary>
    /// Persists the page-sequence JSON for a <c>sourceData</c> job.
    /// Stores the ordered list of page entries (including <c>pdf</c>-embed and
    /// custom-type pages) together with the document title and resolved custom-type
    /// messages. Used by the PDF builder to reconstruct the full page sequence,
    /// including pages that have no canvas in the synthesised Manifest.
    /// </summary>
    Task SavePageSequence(string key, string json);

    /// <summary>
    /// Loads the page-sequence JSON for the given key, or <see langword="null"/> when
    /// none exists. A null result means the job was submitted with <c>sourceUri</c>
    /// (or pre-dates this feature) and the PDF builder should fall back to the
    /// manifest-based path.
    /// </summary>
    Task<string?> LoadPageSequence(string key);

    /// <summary>
    /// Deletes all stored artefacts for the given key (text index, autocomplete, manifest,
    /// raw text, PDF, figures, annotations, capabilities, page sequence).
    /// Missing artefacts are silently ignored — the operation is idempotent.
    /// Call before re-running a job to prevent stale derivatives from a previous build
    /// from persisting when the new build produces different outputs.
    /// </summary>
    Task DeleteArtefacts(string key);
}
