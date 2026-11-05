namespace TextServices.Builder.Api.Services;

public sealed class ManifestFetcher(IResourceFetcher fetcher, IManifestReducer reducer) : IManifestFetcher
{
    public async Task<ManifestFetchResult> FetchAndReduce(string uri, CancellationToken ct)
    {
        await using var stream = await fetcher.FetchAsync(uri, ct)
            ?? throw new InvalidOperationException($"Manifest not found at: {uri}");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(ct);
        var reduced = reducer.Reduce(json);
        return new ManifestFetchResult(json, reduced.Pages, reduced.SkippedCanvases);
    }
}
