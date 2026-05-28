using System.Xml.Linq;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Search.Api.Features.Search;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

/// <summary>
/// Tests for <see cref="SearchHandler"/> — verifies IIIF Search v1 response
/// shape using real <see cref="Text"/> objects built from inline ALTO.
/// </summary>
public class SearchHandlerTests
{
    private const string SelfUrl = "https://search.example.org/search/v1/test/book";

    // -------------------------------------------------------------------------
    // No results
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_EmptyQuery_ReturnsEmptyResponse()
    {
        var text = BuildText([("https://example.org/c/1", 1000, 1500, "hello world")]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Resources.ShouldBeEmpty();
        result.Hits.ShouldBeEmpty();
        result.Within.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_QueryNotFound_ReturnsEmptyResponse()
    {
        var text = BuildText([("https://example.org/c/1", 1000, 1500, "hello world")]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "parliament", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Resources.ShouldBeEmpty();
        result.Hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_TextNotFound_ReturnsNull()
    {
        var handler = new SearchHandler(new StubTextCache(null));

        var result = await handler.Handle(
            new SearchRequest("missing/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Response shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_SingleHit_CorrectAnnotationShape()
    {
        var canvasId = "https://example.org/c/1";
        var text = BuildText([(canvasId, 1000, 1500, "the quick brown fox")]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "quick", SelfUrl, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Resources.Count.ShouldBe(1);

        var anno = result.Resources[0];
        anno.Type.ShouldBe("oa:Annotation");
        anno.Motivation.ShouldBe("sc:painting");
        anno.Resource.Type.ShouldBe("cnt:ContentAsText");
        anno.Resource.Chars.ShouldBe("quick");
        anno.On.ShouldStartWith(canvasId);
        anno.On.ShouldContain("#xywh=");
        anno.Id.ShouldStartWith(SelfUrl);
    }

    [Fact]
    public async Task Handle_SingleHit_CorrectHitShape()
    {
        var text = BuildText([("https://example.org/c/1", 1000, 1500, "the quick brown fox")]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "quick", SelfUrl, SelfUrl), CancellationToken.None);

        result!.Hits.Count.ShouldBe(1);

        var hit = result.Hits[0];
        hit.Type.ShouldBe("search:Hit");
        hit.Match.ShouldBe("quick");
        hit.Annotations.Length.ShouldBe(1);
        hit.Annotations[0].ShouldBe(result.Resources[0].Id);
        // Context should be present (the word has neighbours).
        hit.Before.ShouldNotBeNullOrEmpty();
        hit.After.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_ResponseHasCorrectId()
    {
        var text = BuildText([("https://example.org/c/1", 1000, 1500, "hello world")]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "hello", SelfUrl, SelfUrl), CancellationToken.None);

        result!.Id.ShouldBe(SelfUrl);
        result.Context.ShouldBe("http://iiif.io/api/search/1/context.json");
        result.Type.ShouldBe("sc:AnnotationList");
    }

    [Fact]
    public async Task Handle_MultipleHits_CorrectCount()
    {
        // "the" appears twice in this text
        var text = BuildText([("https://example.org/c/1", 1000, 1500, "the quick brown the fox")]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "the", SelfUrl, SelfUrl), CancellationToken.None);

        result!.Resources.Count.ShouldBe(2);
        result.Hits.Count.ShouldBe(2);
        result.Within.Total.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_WithQueryInSelfUrl_AnnotationIdDoesNotContainQueryString()
    {
        var selfUrlWithQuery = $"{SelfUrl}?q=quick";
        var text = BuildText([("https://example.org/c/1", 1000, 1500, "the quick brown fox")]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "quick", selfUrlWithQuery, SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(selfUrlWithQuery);
        var annoId = result.Resources[0].Id;
        annoId.ShouldNotContain("?q=");
        annoId.ShouldStartWith(SelfUrl + "/anno/");
    }

    [Fact]
    public async Task Handle_MultiPage_AnnotationsReferenceCorrectCanvas()
    {
        var canvas1 = "https://example.org/c/1";
        var canvas2 = "https://example.org/c/2";
        var text = BuildText([
            (canvas1, 1000, 1500, "hello world"),
            (canvas2, 1000, 1500, "parliament square"),
        ]);
        var handler = new SearchHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new SearchRequest("test/book", "parliament", SelfUrl, SelfUrl), CancellationToken.None);

        result!.Resources.Count.ShouldBe(1);
        result.Resources[0].On.ShouldStartWith(canvas2);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="Text"/> from a list of (canvasId, width, height, words) tuples
    /// using <see cref="TextBuilder"/> with inline ALTO XML.
    /// </summary>
    private static Text BuildText(IEnumerable<(string Id, int W, int H, string Words)> pages)
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
