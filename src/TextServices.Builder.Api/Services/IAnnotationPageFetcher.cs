namespace TextServices.Builder.Api.Services;

/// <summary>
/// Fetches an externally-referenced IIIF AnnotationPage JSON document by URI.
/// </summary>
public interface IAnnotationPageFetcher
{
    /// <summary>
    /// Returns the raw JSON string for the AnnotationPage at <paramref name="uri"/>,
    /// or <see langword="null"/> if the resource returns HTTP 404.
    /// Throws for other failure status codes.
    /// </summary>
    Task<string?> FetchAsync(string uri, CancellationToken ct);
}
