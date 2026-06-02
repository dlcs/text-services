using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace TextServices.Builder.Api.Services;

/// <summary>
/// Routes fetch requests to the correct transport based on URI scheme.
/// <list type="bullet">
///   <item><c>http://</c> / <c>https://</c> — HttpClient ("Resource" named instance)</item>
///   <item><c>file://</c> — local filesystem via <see cref="File.OpenRead"/></item>
///   <item><c>s3://bucket/key</c> — Amazon S3 (requires IAmazonS3 to be registered)</item>
/// </list>
/// </summary>
public sealed class ResourceFetcher(IHttpClientFactory httpClientFactory, IAmazonS3? s3) : IResourceFetcher
{
    public Task<Stream?> FetchAsync(string uri, CancellationToken ct = default)
    {
        var parsed = new Uri(uri);
        return parsed.Scheme.ToLowerInvariant() switch
        {
            "file" => Task.FromResult(FetchFile(parsed)),
            "s3" => FetchS3Async(parsed, ct),
            "http" or "https" => FetchHttpAsync(uri, ct),
            var scheme => throw new NotSupportedException($"Unsupported URI scheme '{scheme}': {uri}"),
        };
    }

    // ---- file:// ---------------------------------------------------------------

    private static Stream? FetchFile(Uri uri)
    {
        // Uri.LocalPath handles platform path conversion:
        //   file:///C:/foo/bar.xml  →  C:\foo\bar.xml  (Windows)
        //   file:///var/data/a.xml  →  /var/data/a.xml (Unix)
        var path = uri.LocalPath;
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    // ---- s3:// -----------------------------------------------------------------

    private async Task<Stream?> FetchS3Async(Uri uri, CancellationToken ct)
    {
        if (s3 is null)
        {
            throw new InvalidOperationException($"s3:// URI encountered but IAmazonS3 is not configured: {uri}");
        }

        var bucket = uri.Host;
        var key = uri.AbsolutePath.TrimStart('/');

        try
        {
            var response = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ---- http:// / https:// ----------------------------------------------------

    private async Task<Stream?> FetchHttpAsync(string uri, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("Resource");
        var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }
}
