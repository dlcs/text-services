using System.Net;
using Microsoft.Extensions.Logging;

namespace TextServices.Builder.Api.Services;

public class AnnotationPageFetcher(
    IHttpClientFactory httpClientFactory,
    ILogger<AnnotationPageFetcher> logger) : IAnnotationPageFetcher
{
    public async Task<string?> FetchAsync(string uri, CancellationToken ct = default)
    {
        logger.LogDebug("Fetching annotation page: {Uri}", uri);
        var client   = httpClientFactory.CreateClient("AnnotationPage");
        var response = await client.GetAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
