using TextServices.Core.Models;
using TextServices.Storage;

namespace TextServices.Search.Api.Services;

/// <summary>
/// Memory-cached access to <see cref="Text"/>, <see cref="AutoComplete"/>, and
/// <see cref="JobServices"/> capability artefacts.
/// Abstracts the storage + cache + thundering-herd protection layers.
/// </summary>
public interface ITextCache
{
    /// <summary>
    /// Returns the <see cref="Text"/> for <paramref name="key"/>, loading and caching it
    /// on first access. Returns <see langword="null"/> if no artefact exists for the key.
    /// </summary>
    Task<Text?> GetTextAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Returns the <see cref="AutoComplete"/> for <paramref name="key"/>, loading and
    /// caching it on first access. Returns <see langword="null"/> if no artefact exists.
    /// </summary>
    Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Returns the <see cref="JobServices"/> capabilities for <paramref name="key"/>,
    /// loading and caching on first access. Returns <see langword="null"/> when no
    /// capabilities file exists — callers should treat all services as enabled in that case.
    /// </summary>
    Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all cached entries for <paramref name="key"/> (text, autocomplete, capabilities).
    /// Called when artefacts are deleted or reprocessed so the next request loads fresh data.
    /// </summary>
    void Invalidate(string key);
}
