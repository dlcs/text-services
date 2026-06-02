namespace TextServices.Builder.Api.Services;

/// <summary>
/// Protocol-dispatching resource fetcher.  Supports <c>http://</c>, <c>https://</c>,
/// <c>file://</c>, and <c>s3://</c> URIs, routing each to the appropriate transport.
/// </summary>
public interface IResourceFetcher
{
    /// <summary>
    /// Fetches the resource at <paramref name="uri"/> and returns its content as a
    /// readable stream, or <see langword="null"/> when the resource does not exist
    /// (HTTP 404, file not found, S3 NoSuchKey).
    /// </summary>
    /// <exception cref="NotSupportedException">URI scheme is not recognised.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>s3://</c> URI was given but S3 is not configured.
    /// </exception>
    Task<Stream?> FetchAsync(string uri, CancellationToken ct);
}
