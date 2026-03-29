using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;

namespace TextServices.Tests.E2E.Infrastructure;

/// <summary>
/// Test implementation of <see cref="IManifestFetcher"/> that serves manifest JSON
/// from local fixture files rather than making real HTTP requests.
///
/// URL pattern expected:
///   https://iiif.wellcomecollection.org/presentation/{bnumber}
/// Maps to:
///   {FixturesRoot}/{bnumber}/manifest.json
/// </summary>
public class FixtureManifestFetcher(string fixturesRoot, IManifestReducer reducer)
    : IManifestFetcher
{
    public async Task<ManifestFetchResult> FetchAndReduce(string uri, CancellationToken ct = default)
    {
        var localPath = UriToLocalPath(uri);
        if (localPath == null || !File.Exists(localPath))
            throw new FileNotFoundException($"No fixture manifest for URI: {uri}");

        var json  = await File.ReadAllTextAsync(localPath, ct);
        var pages = reducer.Reduce(json);
        return new ManifestFetchResult(json, pages);
    }

    private string? UriToLocalPath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return null;

        var bnumber = parsed.AbsolutePath.Trim('/').Split('/').LastOrDefault();
        if (string.IsNullOrEmpty(bnumber)) return null;

        return Path.Combine(fixturesRoot, bnumber, "manifest.json");
    }
}
