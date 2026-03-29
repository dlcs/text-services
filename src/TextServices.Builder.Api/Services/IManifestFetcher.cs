using TextServices.Builder.Api.Features.Jobs;

namespace TextServices.Builder.Api.Services;

/// <summary>
/// Fetches a IIIF Presentation v3 Manifest from a URI and reduces it to a
/// flat page sequence.
/// </summary>
public interface IManifestFetcher
{
    /// <summary>
    /// Downloads the manifest at <paramref name="uri"/>, stores the raw JSON, and
    /// returns both the JSON string and the reduced page sequence.
    /// </summary>
    Task<ManifestFetchResult> FetchAndReduce(string uri, CancellationToken ct = default);
}

/// <param name="Json">The raw manifest JSON as returned by the server.</param>
/// <param name="Pages">Reduced page sequence — one entry per canvas.</param>
public record ManifestFetchResult(string Json, IReadOnlyList<PageInstruction> Pages);
