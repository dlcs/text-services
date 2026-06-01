using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features;

/// <summary>
/// Holds the resolved URL components for a single endpoint request, accounting for
/// <c>X-Forwarded-Host</c> and <c>X-Forwarded-Path</c> proxy headers.
/// </summary>
/// <param name="EffectiveId">
/// Job id to use in generated IIIF URLs. Extracted from <c>X-Forwarded-Path</c> when a
/// whitelisted host is present; otherwise equals the original route id.
/// </param>
/// <param name="SelfUrl">Absolute URL for this endpoint response (base + route prefix + effective id + query).</param>
/// <param name="ResourceUrl">Absolute URL without query string. Used as the base for child resource IDs (e.g. annotations).</param>
/// <param name="BaseUrl">Scheme + authority only (no path). Configured URL or based on x-forwarded-host if valid.</param>
internal record ResolvedRequest(string EffectiveId, string SelfUrl, string ResourceUrl, string BaseUrl);

internal static class EndpointHelpers
{
    /// <summary>
    /// Resolves the effective id, self URL, and base URL for an endpoint in a single pass over
    /// the forwarded headers.
    /// </summary>
    /// <param name="ctx">Current HTTP context object</param>
    /// <param name="routePrefix">
    /// The path segment before <c>{**id}</c>, e.g. <c>"search/v1/"</c> or
    /// <c>"annotations/lines/v1/3/"</c>. Used to strip the prefix from
    /// <c>X-Forwarded-Path</c> to extract the forwarded id.
    /// </param>
    /// <param name="originalId">The job id from the route parameter.</param>
    /// <param name="q">Optional query term, appended as <c>?q=…</c> on the self URL.</param>
    /// <param name="opts"><see cref="SearchApiOptions"/> object</param>
    internal static ResolvedRequest Resolve(SearchApiOptions opts, HttpContext ctx, string routePrefix,
        string originalId, string? q = null)
    {
        var forwardedHost = ctx.Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        var forwardedPath = ctx.Request.Headers["X-Forwarded-Path"].FirstOrDefault();

        var baseUrl = ResolveBaseUrl(opts, ctx, forwardedHost);
        var effectiveId = ResolveId(forwardedHost, forwardedPath, opts.AllowedCustomHosts, routePrefix, originalId);

        var url = $"{baseUrl}/{routePrefix.TrimEnd('/')}/{effectiveId}";
        var selfUrl = string.IsNullOrWhiteSpace(q) ? url : $"{url}?q={Uri.EscapeDataString(q)}";

        return new ResolvedRequest(effectiveId, selfUrl, url, baseUrl);
    }

    internal static string[]? GetIgnoredParams(HttpContext ctx)
    {
        string[] knownIgnored = ["motivation", "date", "user", "box"];
        var ignored = ctx.Request.Query.Keys
            .Where(k => knownIgnored.Contains(k, StringComparer.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(ctx.Request.Query[k]))
            .ToArray();
        return ignored.Length > 0 ? ignored : null;
    }

    private static string ResolveBaseUrl(SearchApiOptions opts, HttpContext ctx, string? forwardedHost)
    {
        if (!string.IsNullOrEmpty(opts.BaseUrl))
        {
            var baseUri = new Uri(opts.BaseUrl);
            var host = IsAllowedCustomHost(forwardedHost, opts.AllowedCustomHosts)
                ? forwardedHost!
                : baseUri.Authority;
            return $"{baseUri.Scheme}://{host}";
        }

        var effectiveHost = IsAllowedCustomHost(forwardedHost, opts.AllowedCustomHosts)
            ? forwardedHost!
            : ctx.Request.Host.ToString();
        return $"{ctx.Request.Scheme}://{effectiveHost}";
    }

    private static string ResolveId(
        string? forwardedHost, string? forwardedPath, string[] allowlist, string routePrefix, string originalId)
    {
        if (!IsAllowedCustomHost(forwardedHost, allowlist)) return originalId;
        if (string.IsNullOrEmpty(forwardedPath)) return originalId;

        var pathOnly = forwardedPath.Split('?')[0];
        var normalised = pathOnly.TrimStart('/');
        var prefix = routePrefix.TrimStart('/');

        if (!normalised.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return originalId;

        var extracted = normalised[prefix.Length..].TrimStart('/');
        return string.IsNullOrEmpty(extracted) ? originalId : extracted;
    }

    private static bool IsAllowedCustomHost(string? host, string[] allowlist)
        => !string.IsNullOrEmpty(host)
           && allowlist.Contains(host, StringComparer.OrdinalIgnoreCase);
}
