using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Search.Api.Features.Annotations;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

/// <summary>
/// Tests for <see cref="WordAnnotationsHandler"/>.
/// </summary>
public class WordAnnotationsHandlerTests
{
    private const string SelfUrl = "https://search.example.org/annotations/words/v1/0/test/book";

    // -------------------------------------------------------------------------
    // 404 cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TextNotFound_ReturnsNull()
    {
        var handler = new WordAnnotationsHandler(new StubTextCache(null));
        var result  = await handler.Handle(new WordAnnotationsRequest("missing", 0, SelfUrl), default);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_CanvasIndexOutOfRange_ReturnsNull()
    {
        var text    = BuildText("https://example.org/c/1", ["hello world"]);
        var handler = new WordAnnotationsHandler(new StubTextCache(text));
        var result  = await handler.Handle(new WordAnnotationsRequest("test/book", 99, SelfUrl), default);
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Response shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsAnnotationPageWithWordGranularity()
    {
        var text    = BuildText("https://example.org/c/1", ["hello world"]);
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 0, SelfUrl), default);

        result.ShouldNotBeNull();
        result["type"]!.GetValue<string>().ShouldBe("AnnotationPage");
        result["textGranularity"]!.GetValue<string>().ShouldBe("word");
    }

    [Fact]
    public async Task Handle_OneAnnotationPerWord()
    {
        var text    = BuildText("https://example.org/c/1", ["hello world foo"]);
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 0, SelfUrl), default);

        result!["items"]!.AsArray().Count.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_MultiLine_StillOneAnnotationPerWord()
    {
        var text    = BuildText("https://example.org/c/1", ["hello world", "foo bar"]);
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 0, SelfUrl), default);

        result!["items"]!.AsArray().Count.ShouldBe(4);
    }

    [Fact]
    public async Task Handle_EachAnnotation_TargetsIndividualWordBox()
    {
        // Two words on the same line at different x positions.
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1");
        acc.NextLine();
        acc.AddWord("hello", "hello", 10, 20, 50, 30);
        acc.AddWord("world", "world", 70, 20, 60, 30);
        var text    = acc.Build().Text;
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 0, SelfUrl), default);

        var items = result!["items"]!.AsArray();
        items[0]!["target"]!.GetValue<string>().ShouldBe("https://example.org/c/1#xywh=10,20,50,30");
        items[1]!["target"]!.GetValue<string>().ShouldBe("https://example.org/c/1#xywh=70,20,60,30");
    }

    [Fact]
    public async Task Handle_AnnotationBody_ContainsSingleWordRawText()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1");
        acc.NextLine();
        acc.AddWord("Hello,", Text.Normalise("Hello,"), 0, 0, 50, 20);
        var text    = acc.Build().Text;
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 0, SelfUrl), default);

        result!["items"]!.AsArray()[0]!["body"]!["value"]!
            .GetValue<string>().ShouldBe("Hello,");
    }

    [Fact]
    public async Task Handle_AnnotationIds_AreSequential()
    {
        var text    = BuildText("https://example.org/c/1", ["a b c"]);
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 0, SelfUrl), default);

        var items = result!["items"]!.AsArray();
        items[0]!["id"]!.GetValue<string>().ShouldBe($"{SelfUrl}/anno/0");
        items[2]!["id"]!.GetValue<string>().ShouldBe($"{SelfUrl}/anno/2");
    }

    [Fact]
    public async Task Handle_MultiCanvas_OnlyReturnsWordsForRequestedCanvas()
    {
        var text = BuildText([
            ("https://example.org/c/1", ["hello world"]),
            ("https://example.org/c/2", ["foo bar baz qux"]),
        ]);
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 1,
            "https://search.example.org/annotations/words/v1/1/test/book"), default);

        var items = result!["items"]!.AsArray();
        items.Count.ShouldBe(4);
        items[0]!["target"]!.GetValue<string>().ShouldStartWith("https://example.org/c/2");
    }

    // -------------------------------------------------------------------------
    // Temporal content
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TemporalWord_TargetUsesTimeFragment()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1", isTemporalContent: true);
        acc.NextLine();
        acc.AddWord("hello", "hello", 2000, 4500);
        acc.AddWord("world", "world", 4500, 7000);
        var text    = acc.Build().Text;
        var handler = new WordAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new WordAnnotationsRequest("test/book", 0, SelfUrl), default);

        var items = result!["items"]!.AsArray();
        items[0]!["target"]!.GetValue<string>().ShouldBe("https://example.org/c/1#t=2,4.5");
        items[1]!["target"]!.GetValue<string>().ShouldBe("https://example.org/c/1#t=4.5,7");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Text BuildText(string canvasId, IEnumerable<string> lines)
        => BuildText([(canvasId, lines)]);

    private static Text BuildText(
        IEnumerable<(string CanvasId, IEnumerable<string> Lines)> pages)
    {
        var acc = new TextAccumulator();
        foreach (var (canvasId, lines) in pages)
        {
            acc.BeginPage(canvasId);
            var y = 0;
            foreach (var line in lines)
            {
                acc.NextLine();
                var x = 0;
                foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    acc.AddWord(word, Text.Normalise(word), x, y, 50, 20);
                    x += 60;
                }
                y += 30;
            }
        }
        return acc.Build().Text;
    }

    private sealed class StubTextCache(Text? text) : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult(text);
        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult<AutoComplete?>(null);
        public Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default)
            => Task.FromResult<JobServices?>(null);
    }
}
