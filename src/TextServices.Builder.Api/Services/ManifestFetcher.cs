namespace TextServices.Builder.Api.Services;

public sealed class ManifestFetcher(IResourceFetcher fetcher, IManifestReducer reducer) : IManifestFetcher
{
    public async Task<ManifestFetchResult> FetchAndReduce(string uri, CancellationToken ct = default)
    {
        await using var stream = await fetcher.FetchAsync(uri, ct)
            ?? throw new InvalidOperationException($"Manifest not found at: {uri}");
        using var reader = new StreamReader(stream);
        var json  = await reader.ReadToEndAsync(ct);
        var pages = reducer.Reduce(json);
        return new ManifestFetchResult(json, pages);
    }
}
