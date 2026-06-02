namespace TextServices.Builder.Api.Services;

public sealed class AnnotationPageFetcher(
    IResourceFetcher fetcher,
    ILogger<AnnotationPageFetcher> logger) : IAnnotationPageFetcher
{
    public async Task<string?> FetchAsync(string uri, CancellationToken ct)
    {
        logger.LogDebug("Fetching annotation page: {Uri}", uri);
        await using var stream = await fetcher.FetchAsync(uri, ct);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
