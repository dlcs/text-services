namespace TextServices.Search.Api;

/// <summary>
/// A minimal 1×1 transparent PNG returned by /proxy/image for URI schemes that
/// cannot be proxied (e.g. s3://).  Allows synthesised manifests to remain
/// structurally valid so IIIF viewers can still render the text layer.
/// </summary>
internal static class ProxyImagePlaceholder
{
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
}
