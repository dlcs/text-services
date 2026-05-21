using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Services;

internal static class ProxyEndpoints
{
    internal static IEndpointRouteBuilder MapProxyEndpoints(this IEndpointRouteBuilder routes)
    {
        // Proxies local file:// image URIs so IIIF viewers can load painting annotation bodies
        // from synthesised manifests.  For non-proxiable schemes (e.g. s3://) returns a 1×1
        // transparent PNG placeholder so the manifest remains structurally valid.
        // The Search API hosts this endpoint (not the Builder API) because the Search API is
        // always running when a viewer needs to load images from a stored manifest.
        routes.MapGet("/proxy/image", async (string uri, IOptions<SearchApiOptions> options, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                return Results.BadRequest("Invalid URI.");

            if (parsed.Scheme == "file")
            {
                if (!options.Value.AllowFileImageProxy)
                    return Results.Bytes(ProxyImagePlaceholder.Png, "image/png");

                var path = parsed.LocalPath;
                if (!File.Exists(path)) return Results.NotFound();

                var ext = Path.GetExtension(path).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".tif" or ".tiff" => "image/tiff",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream",
                };
                return Results.Stream(File.OpenRead(path), contentType);
            }

            if (parsed.Scheme == "s3")
                return Results.Bytes(ProxyImagePlaceholder.Png, "image/png");

            return Results.BadRequest($"URI scheme '{parsed.Scheme}' is not supported by this proxy.");
        });

        return routes;
    }
}
