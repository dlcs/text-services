using TextServices.Core.Models;

namespace TextServices.Search.Api.Services;

/// <summary>
/// Memory-cached access to <see cref="Text"/> and <see cref="AutoComplete"/> artefacts.
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
}
