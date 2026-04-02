using System.Net;
using Microsoft.Extensions.Logging;

namespace TextServices.Builder.Api.Services;

public class VttFetcher(IHttpClientFactory httpClientFactory, ILogger<VttFetcher> logger) : IVttFetcher
{
    public async Task<string?> FetchAsync(string uri, CancellationToken ct = default)
    {
        logger.LogDebug("Fetching VTT: {Uri}", uri);
        var client   = httpClientFactory.CreateClient("Vtt");
        var response = await client.GetAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
