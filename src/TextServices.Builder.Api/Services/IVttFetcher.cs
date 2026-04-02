namespace TextServices.Builder.Api.Services;

/// <summary>
/// Fetches a WebVTT (or other plain-text transcript) resource by URI.
/// </summary>
public interface IVttFetcher
{
    /// <summary>
    /// Fetches the raw text content of the resource at <paramref name="uri"/>.
    /// Returns <see langword="null"/> for HTTP 404 (sparse page).
    /// Throws for other failure conditions.
    /// </summary>
    Task<string?> FetchAsync(string uri, CancellationToken ct = default);
}
