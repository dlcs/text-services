using System.Xml.Linq;

namespace TextServices.Builder.Api.Services;

public sealed class AltoFetcher(IResourceFetcher fetcher) : IAltoFetcher
{
    public async Task<XElement?> FetchAsync(string uri, CancellationToken ct = default)
    {
        await using var stream = await fetcher.FetchAsync(uri, ct);
        if (stream is null) return null;

        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync(ct);
        return string.IsNullOrWhiteSpace(xml) ? null : XElement.Parse(xml);
    }
}
