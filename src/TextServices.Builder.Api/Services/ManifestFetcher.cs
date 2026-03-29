namespace TextServices.Builder.Api.Services;

public class ManifestFetcher(IHttpClientFactory httpClientFactory, IManifestReducer reducer)
    : IManifestFetcher
{
    public async Task<ManifestFetchResult> FetchAndReduce(string uri, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("Manifest");
        var json = await client.GetStringAsync(uri, ct);
        var pages = reducer.Reduce(json);
        return new ManifestFetchResult(json, pages);
    }
}
