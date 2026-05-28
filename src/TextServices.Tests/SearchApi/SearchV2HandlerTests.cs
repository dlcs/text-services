using System.Xml.Linq;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Search.Api.Features.Search;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

/// <summary>
/// Tests for <see cref="SearchV2Handler"/> — verifies IIIF Search v2 response
/// shape for both spatial (ALTO) and temporal (VTT) hits.
/// </summary>
public class SearchV2HandlerTests
{
    private const string SelfUrl = "https://search.example.org/search/v2/test/book";

    // -------------------------------------------------------------------------
    // No results / not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TextNotFound_ReturnsNull()
    {
        var handler = new SearchV2Handler(new StubTextCache(null));

        var result = await handler.Handle(
            new SearchV2Request("missing/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_EmptyQuery_ReturnsEmptyResponse()
    {
        var text = BuildSpatialText([("https://example.org/c/1", 1000, 1500, "hello world")]);
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Items.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Temporal hit — motivation, target, fragment format
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TemporalHit_MotivationIsSupplementing()
    {
        var canvasId = "https://example.org/c/audio";
        var text = BuildTemporalText(canvasId, "00:00:05.000 --> 00:00:08.500\nHello world\n");
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Items.Count.ShouldBe(1);
        result.Items[0].Motivation.ShouldBe("supplementing");
    }

    [Fact]
    public async Task Handle_TemporalHit_TargetUsesTemporalFragment()
    {
        var canvasId = "https://example.org/c/audio";
        var text = BuildTemporalText(canvasId, "00:00:05.000 --> 00:00:08.500\nHello world\n");
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        var target = result.Items[0].Target;
        target.ShouldContain("#t=");
        target.ShouldNotContain("#xywh=");
    }

    [Fact]
    public async Task Handle_TemporalHit_FragmentUsesDecimalPoint()
    {
        var canvasId = "https://example.org/c/audio";
        var text = BuildTemporalText(canvasId, "00:00:05.500 --> 00:00:08.750\nHello world\n");
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        var target = result.Items[0].Target;
        // Decimal separator must be "." (invariant culture), never ","
        target.ShouldContain(".");
        target.ShouldNotContain(",5");
        target.ShouldNotContain(",7");
    }

    [Fact]
    public async Task Handle_TemporalHit_TimesAreInSeconds()
    {
        var canvasId = "https://example.org/c/audio";
        // StartMs = 5000 → 5 seconds
        var text = BuildTemporalText(canvasId, "00:00:05.000 --> 00:00:08.500\nHello world\n");
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        var target = result.Items[0].Target;
        target.ShouldContain("#t=5,");
    }

    // -------------------------------------------------------------------------
    // Spatial hit — motivation and fragment
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_SpatialHit_MotivationIsPainting()
    {
        var canvasId = "https://example.org/c/1";
        var text = BuildSpatialText([(canvasId, 1000, 1500, "the quick brown fox")]);
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "quick", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Items.Count.ShouldBe(1);
        result.Items[0].Motivation.ShouldBe("painting");
        result.Items[0].Target.ShouldContain("#xywh=");
        result.Items[0].Target.ShouldNotContain("#t=");
    }

    [Fact]
    public async Task Handle_WithQueryInSelfUrl_AnnotationIdDoesNotContainQueryString()
    {
        var selfUrlWithQuery = $"{SelfUrl}?q=quick";
        var canvasId = "https://example.org/c/1";
        var text = BuildSpatialText([(canvasId, 1000, 1500, "the quick brown fox")]);
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "quick", selfUrlWithQuery, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(selfUrlWithQuery);
        var annoId = result.Items[0].Id;
        annoId.ShouldNotContain("?q=");
        annoId.ShouldStartWith(SelfUrl + "/anno/");
    }

    [Fact]
    public async Task Handle_WithQueryInSelfUrl_ContextualizingAnnotationIdDoesNotContainQueryString()
    {
        var selfUrlWithQuery = $"{SelfUrl}?q=quick";
        var canvasId = "https://example.org/c/1";
        var text = BuildSpatialText([(canvasId, 1000, 1500, "the quick brown fox")]);
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "quick", selfUrlWithQuery, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Annotations.ShouldNotBeNull();
        var contextId = result.Annotations![0].Items[0].Id;
        contextId.ShouldNotContain("?q=");
        contextId.ShouldStartWith(SelfUrl + "/context/");
    }

    // -------------------------------------------------------------------------
    // Contextualizing annotations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TemporalHit_ContextualizingAnnotationPresent()
    {
        var canvasId = "https://example.org/c/audio";
        var text = BuildTemporalText(canvasId, "00:00:05.000 --> 00:00:08.500\nHello world\n");
        var handler = new SearchV2Handler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchV2Request("test/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Annotations.ShouldNotBeNull();
        result.Annotations!.Count.ShouldBeGreaterThan(0);
        result.Annotations[0].Items.Count.ShouldBeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a temporal <see cref="Text"/> from a canvas id and raw VTT cue block
    /// (the lines after the WEBVTT header, without the header itself).
    /// </summary>
    private static Text BuildTemporalText(string canvasId, string cueBlock)
    {
        var vtt = $"WEBVTT\n\n{cueBlock}\n";
        var builder = new TextBuilder();
        builder.AddTranscriptPage(canvasId, 0, 0, vtt, format: "text/vtt");
        return builder.Build().Text;
    }

    /// <summary>
    /// Builds a spatial <see cref="Text"/> from a list of (canvasId, width, height, words) tuples
    /// using <see cref="TextBuilder"/> with inline ALTO XML.
    /// </summary>
    private static Text BuildSpatialText(IEnumerable<(string Id, int W, int H, string Words)> pages)
    {
        XNamespace ns = "http://www.loc.gov/standards/alto/ns-v3#";
        var builder = new TextBuilder();

        foreach (var (id, w, h, words) in pages)
        {
            int x = 0;
            var strings = words.Split(' ').Select(word =>
            {
                var el = new XElement(ns + "String",
                    new XAttribute("CONTENT", word),
                    new XAttribute("HPOS", x),
                    new XAttribute("VPOS", 10),
                    new XAttribute("WIDTH", 50),
                    new XAttribute("HEIGHT", 20));
                x += 60;
                return el;
            }).ToArray<object>();

            var alto = new XElement(ns + "alto",
                new XElement(ns + "Layout",
                    new XElement(ns + "Page",
                        new XAttribute("WIDTH", w),
                        new XAttribute("HEIGHT", h),
                        new XElement(ns + "PrintSpace",
                            new XElement(ns + "TextBlock",
                                new XElement(ns + "TextLine", strings))))));

            builder.AddPage(id, w, h, alto, profile: "alto");
        }

        return builder.Build().Text;
    }

    private sealed class StubTextCache(Text? text) : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult(text);

        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult<AutoComplete?>(null);
        public Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default)
            => Task.FromResult<JobServices?>(null);
        public void Invalidate(string key) { }
    }
}
