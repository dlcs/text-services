using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features;

internal static class EndpointHelpers
{
    internal static string BuildSelfUrl(SearchApiOptions opts, HttpContext ctx, string path, string? q)
    {
        var baseUrl = string.IsNullOrEmpty(opts.BaseUrl)
            ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
            : opts.BaseUrl.TrimEnd('/');

        var url = $"{baseUrl}/{path}";
        return string.IsNullOrWhiteSpace(q) ? url : $"{url}?q={Uri.EscapeDataString(q)}";
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
}
