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
