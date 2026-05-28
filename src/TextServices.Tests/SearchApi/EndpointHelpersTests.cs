using Microsoft.AspNetCore.Http;
using Shouldly;
using TextServices.Search.Api.Configuration;
using TextServices.Search.Api.Features;

namespace TextServices.Tests.SearchApi;

public class EndpointHelpersTests
{
    // -------------------------------------------------------------------------
    // Resolve — no forwarding (baseline)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_NoForwardedHeaders_UsesBaseUrlAndOriginalId()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("2/cc/123");
        result.SelfUrl.ShouldBe("https://canonical.search/search/v1/2/cc/123");
        result.BaseUrl.ShouldBe("https://canonical.search");
    }

    [Fact]
    public void Resolve_NoBaseUrl_UsesRequestSchemeAndHost()
    {
        var opts = Opts();
        var result = EndpointHelpers.Resolve(opts, Context(scheme: "https", host: "request.host"), "search/v1/", "a/b");
        result.SelfUrl.ShouldBe("https://request.host/search/v1/a/b");
        result.BaseUrl.ShouldBe("https://request.host");
    }

    [Fact]
    public void Resolve_QueryParam_AppendedToSelfUrl()
    {
        var opts = Opts(baseUrl: "https://canonical.search");
        var result = EndpointHelpers.Resolve(opts, Context(), "search/v1/", "a/b", "hello world");
        result.SelfUrl.ShouldBe("https://canonical.search/search/v1/a/b?q=hello%20world");
    }

    // -------------------------------------------------------------------------
    // Resolve — X-Forwarded-Host only
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_KnownForwardedHost_ReplacesHostKeepsOriginalId()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("2/cc/123");
        result.SelfUrl.ShouldBe("https://known.host/search/v1/2/cc/123");
        result.BaseUrl.ShouldBe("https://known.host");
    }

    [Fact]
    public void Resolve_UnknownForwardedHost_Ignored()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "evil.com"), "search/v1/", "2/cc/123");
        result.SelfUrl.ShouldBe("https://canonical.search/search/v1/2/cc/123");
        result.BaseUrl.ShouldBe("https://canonical.search");
    }

    [Fact]
    public void Resolve_EmptyAllowlist_ForwardedHostIgnored()
    {
        var opts = Opts(baseUrl: "https://canonical.search");
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host"), "search/v1/", "2/cc/123");
        result.BaseUrl.ShouldBe("https://canonical.search");
    }

    // -------------------------------------------------------------------------
    // Resolve — X-Forwarded-Host + X-Forwarded-Path (id extraction)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_KnownHostAndPath_ExtractsIdFromPath()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host", forwardedPath: "search/v1/cc/123"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("cc/123");
        result.SelfUrl.ShouldBe("https://known.host/search/v1/cc/123");
    }

    [Fact]
    public void Resolve_ForwardedPathWithLeadingSlash_Handled()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host", forwardedPath: "/search/v1/cc/123"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("cc/123");
    }

    [Fact]
    public void Resolve_ForwardedPathWithQueryString_QueryStripped()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host", forwardedPath: "search/v1/cc/123?q=test"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("cc/123");
        result.SelfUrl.ShouldNotContain("test");
    }

    [Fact]
    public void Resolve_ForwardedPathPrefixMismatch_ReturnsOriginalId()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host", forwardedPath: "autocomplete/v1/cc/123"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("2/cc/123");
    }

    [Fact]
    public void Resolve_ForwardedPathButUnknownHost_PathIgnored()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "unknown.host", forwardedPath: "search/v1/cc/123"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("2/cc/123");
        result.BaseUrl.ShouldBe("https://canonical.search");
    }

    [Fact]
    public void Resolve_ForwardedPathWithNoHost_PathIgnored()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedPath: "search/v1/cc/123"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("2/cc/123");
    }

    [Fact]
    public void Resolve_KnownHostCaseInsensitive()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["Known.Host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host", forwardedPath: "search/v1/cc/123"), "search/v1/", "2/cc/123");
        result.EffectiveId.ShouldBe("cc/123");
    }

    [Fact]
    public void Resolve_TextAugmentedRoute_ExtractsId()
    {
        var opts = Opts(baseUrl: "https://canonical.search", allowedHosts: ["known.host"]);
        var result = EndpointHelpers.Resolve(opts, Context(forwardedHost: "known.host", forwardedPath: "text-augmented/v3/cc/123"), "text-augmented/v3/", "2/cc/123");
        result.EffectiveId.ShouldBe("cc/123");
        result.SelfUrl.ShouldBe("https://known.host/text-augmented/v3/cc/123");
        result.BaseUrl.ShouldBe("https://known.host");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SearchApiOptions Opts(string baseUrl = "", string[]? allowedHosts = null) =>
        new() { BaseUrl = baseUrl, AllowedCustomHosts = allowedHosts ?? [] };

    private static HttpContext Context(
        string scheme = "http",
        string host = "localhost",
        string? forwardedHost = null,
        string? forwardedPath = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        if (forwardedHost != null) ctx.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        if (forwardedPath != null) ctx.Request.Headers["X-Forwarded-Path"] = forwardedPath;
        return ctx;
    }
}
