using System.Net;
using System.Xml.Linq;

namespace TextServices.Builder.Api.Services;

public class AltoFetcher(IHttpClientFactory httpClientFactory) : IAltoFetcher
{
    public async Task<XElement?> FetchAsync(string uri, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("Alto");
        var response = await client.GetAsync(uri, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(xml) ? null : XElement.Parse(xml);
    }
}
