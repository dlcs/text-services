using System.Xml.Linq;
using TextServices.Builder.Api.Services;

namespace TextServices.Tests.E2E.Infrastructure;

/// <summary>
/// Test implementation of <see cref="IAltoFetcher"/> that serves ALTO XML from
/// local fixture files rather than making real HTTP requests.
///
/// URL pattern expected:
///   https://api.wellcomecollection.org/text/alto/{bnumber}/{filename}
/// Maps to:
///   {FixturesRoot}/{bnumber}/alto/{filename}.xml
/// </summary>
public class FixtureAltoFetcher(string fixturesRoot) : IAltoFetcher
{
    public Task<XElement?> FetchAsync(string uri, CancellationToken ct = default)
    {
        var localPath = UriToLocalPath(uri);

        if (localPath == null || !File.Exists(localPath))
            return Task.FromResult<XElement?>(null);

        var xml = XElement.Load(localPath);
        return Task.FromResult<XElement?>(xml);
    }

    private string? UriToLocalPath(string uri)
    {
        // Handles: https://api.wellcomecollection.org/text/alto/{bnumber}/{filename}
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return null;

        var segments = parsed.AbsolutePath.TrimStart('/').Split('/');
        // segments: ["text", "alto", "{bnumber}", "{filename}"]
        if (segments.Length < 4) return null;

        var bnumber = segments[^2];
        var filename = segments[^1];

        return Path.Combine(fixturesRoot, bnumber, "alto", filename + ".xml");
    }
}
