using TextServices.Storage;

namespace TextServices.Search.Api.Services;

internal static class CapabilitiesExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="service"/> is enabled for the
    /// given key. When no capabilities file exists (null), no services are enabled: the builder
    /// writes the file only once a job's artefacts are complete.
    /// </summary>
    public static async Task<bool> IsEnabledAsync(
        this ITextCache cache,
        string key,
        JobServices service,
        CancellationToken ct = default)
    {
        var caps = await cache.GetCapabilitiesAsync(key, ct);
        return caps != null && caps.Value.HasFlag(service);
    }
}
