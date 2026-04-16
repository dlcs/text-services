using System.Text.Json.Nodes;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Search.Api.Features.Annotations;
using TextServices.Search.Api.Services;

namespace TextServices.Tests.SearchApi;

/// <summary>
/// Tests for <see cref="LineAnnotationsHandler"/>.
/// </summary>
public class LineAnnotationsHandlerTests
{
    private const string SelfUrl = "https://search.example.org/annotations/lines/v1/0/test/book";

    // -------------------------------------------------------------------------
    // 404 cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TextNotFound_ReturnsNull()
    {
        var handler = new LineAnnotationsHandler(new StubTextCache(null));
        var result  = await handler.Handle(new LineAnnotationsRequest("missing", 0, SelfUrl), default);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_CanvasIndexOutOfRange_ReturnsNull()
    {
        var text    = BuildSpatialText([("https://example.org/c/1", "hello world")]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));
        var result  = await handler.Handle(new LineAnnotationsRequest("test/book", 5, SelfUrl), default);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NegativeCanvasIndex_ReturnsNull()
    {
        var text    = BuildSpatialText([("https://example.org/c/1", "hello world")]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));
        var result  = await handler.Handle(new LineAnnotationsRequest("test/book", -1, SelfUrl), default);
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Response shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsAnnotationPageWithCorrectMetadata()
    {
        var text    = BuildSpatialText([("https://example.org/c/1", "hello world")]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        result.ShouldNotBeNull();
        result["id"]!.GetValue<string>().ShouldBe(SelfUrl);
        result["type"]!.GetValue<string>().ShouldBe("AnnotationPage");
        result["textGranularity"]!.GetValue<string>().ShouldBe("line");
        result["@context"]!.AsArray().Select(n => n!.GetValue<string>())
            .ShouldContain("https://iiif.io/api/extension/text-granularity/context.json");
    }

    [Fact]
    public async Task Handle_SingleLine_ReturnsSingleAnnotation()
    {
        // All words are on one TextLine in ALTO, so Li is the same for all.
        var text    = BuildSpatialText([("https://example.org/c/1", "hello world foo")]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        var items = result!["items"]!.AsArray();
        items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_MultiLine_ReturnsOneAnnotationPerLine()
    {
        // W3C annotations — each annotation is its own line (distinct Li value).
        var text    = BuildSpatialTextMultiLine("https://example.org/c/1",
            ["line one", "line two", "line three"]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        result!["items"]!.AsArray().Count.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_LineAnnotation_ContainsAllWordsInLine()
    {
        var text    = BuildSpatialTextMultiLine("https://example.org/c/1",
            ["prime minister spoke"]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        var body = result!["items"]!.AsArray()[0]!["body"]!;
        body["value"]!.GetValue<string>().ShouldBe("prime minister spoke");
    }

    [Fact]
    public async Task Handle_LineAnnotation_TargetUsesOuterBoundingBox()
    {
        // Words on one line at x=10,y=20,w=50,h=30 and x=70,y=22,w=60,h=28
        // Expected outer box: x=10, y=20, w=(70+60)-10=120, h=max(20+30,22+28)-20=30
        var text = BuildSpatialTextWithCoords("https://example.org/c/1",
            line: 0,
            words: [("hello", 10, 20, 50, 30), ("world", 70, 22, 60, 28)]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        var target = result!["items"]!.AsArray()[0]!["target"]!.GetValue<string>();
        target.ShouldBe("https://example.org/c/1#xywh=10,20,120,30");
    }

    [Fact]
    public async Task Handle_AnnotationIds_PrefixedWithSelfUrl()
    {
        var text    = BuildSpatialTextMultiLine("https://example.org/c/1", ["a", "b"]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        var items = result!["items"]!.AsArray();
        items[0]!["id"]!.GetValue<string>().ShouldBe($"{SelfUrl}/anno/0");
        items[1]!["id"]!.GetValue<string>().ShouldBe($"{SelfUrl}/anno/1");
    }

    [Fact]
    public async Task Handle_AnnotationMotivation_IsSupplementing()
    {
        var text    = BuildSpatialText([("https://example.org/c/1", "hello")]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        result!["items"]!.AsArray()[0]!["motivation"]!
            .GetValue<string>().ShouldBe("supplementing");
    }

    [Fact]
    public async Task Handle_SecondCanvas_ReturnsCorrectCanvasWords()
    {
        var text = BuildSpatialText([
            ("https://example.org/c/1", "page one words"),
            ("https://example.org/c/2", "page two words"),
        ]);
        var selfUrl2 = "https://search.example.org/annotations/lines/v1/1/test/book";
        var handler  = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(
            new LineAnnotationsRequest("test/book", 1, selfUrl2), default);

        var items = result!["items"]!.AsArray();
        items.Count.ShouldBe(1);
        items[0]!["body"]!["value"]!.GetValue<string>().ShouldBe("page two words");
        items[0]!["target"]!.GetValue<string>().ShouldStartWith("https://example.org/c/2");
    }

    [Fact]
    public async Task Handle_SparseCanvas_ReturnsEmptyItems()
    {
        // Build a text where canvas 0 has no words (sparse), canvas 1 does.
        // We request canvas 0 — should return empty items, not 404.
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1");   // sparse — no words added
        acc.BeginPage("https://example.org/c/2");
        acc.NextLine();
        acc.AddWord("hello", "hello", 0, 0, 50, 20);
        var text    = acc.Build().Text;
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        result.ShouldNotBeNull();
        result["items"]!.AsArray().Count.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // Temporal content
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TemporalCanvas_TargetUsesTimeFragment()
    {
        var text = BuildTemporalText("https://example.org/c/1",
            [("hello", 1000, 3000), ("world", 3000, 5500)]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        // One line spanning both words: t=1.000 to t=5.500
        var target = result!["items"]!.AsArray()[0]!["target"]!.GetValue<string>();
        target.ShouldBe("https://example.org/c/1#t=1,5.5");
    }

    [Fact]
    public async Task Handle_TemporalMultiLine_EachCueIsSeparateLine()
    {
        // Two VTT cues = two lines.
        var text = BuildTemporalMultiLine("https://example.org/c/1",
            [("hello world", 0, 3000), ("foo bar", 3000, 6000)]);
        var handler = new LineAnnotationsHandler(new StubTextCache(text));

        var result = await handler.Handle(new LineAnnotationsRequest("test/book", 0, SelfUrl), default);

        var items = result!["items"]!.AsArray();
        items.Count.ShouldBe(2);
        items[0]!["target"]!.GetValue<string>().ShouldBe("https://example.org/c/1#t=0,3");
        items[1]!["target"]!.GetValue<string>().ShouldBe("https://example.org/c/1#t=3,6");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a Text with one TextLine per entry in <paramref name="pages"/>
    /// (all words on each canvas share the same bounding box row at y=10, h=20).
    /// </summary>
    private static Text BuildSpatialText(
        IEnumerable<(string CanvasId, string Words)> pages)
    {
        var acc = new TextAccumulator();
        foreach (var (canvasId, words) in pages)
        {
            acc.BeginPage(canvasId);
            acc.NextLine();
            var x = 0;
            foreach (var word in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                acc.AddWord(word, Text.Normalise(word), x, 10, 50, 20);
                x += 60;
            }
        }
        return acc.Build().Text;
    }

    /// <summary>
    /// Builds a Text with one canvas and multiple lines, one line per entry in
    /// <paramref name="lines"/> (using W3C annotation-style: each line is a NextLine call).
    /// </summary>
    private static Text BuildSpatialTextMultiLine(string canvasId, IEnumerable<string> lines)
    {
        var acc = new TextAccumulator();
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
        return acc.Build().Text;
    }

    /// <summary>
    /// Builds a single-canvas Text with words at explicit pixel coordinates,
    /// all sharing the same line index.
    /// </summary>
    private static Text BuildSpatialTextWithCoords(
        string canvasId, int line,
        IEnumerable<(string Word, int X, int Y, int W, int H)> words)
    {
        var acc = new TextAccumulator();
        acc.BeginPage(canvasId);
        // Advance to the requested line number.
        for (var i = 0; i <= line; i++) acc.NextLine();
        foreach (var (word, x, y, w, h) in words)
            acc.AddWord(word, Text.Normalise(word), x, y, w, h);
        return acc.Build().Text;
    }

    /// <summary>Builds a single-canvas temporal Text with words all on the same cue (line).</summary>
    private static Text BuildTemporalText(
        string canvasId,
        IEnumerable<(string Word, int StartMs, int EndMs)> words)
    {
        var acc = new TextAccumulator();
        acc.BeginPage(canvasId, isTemporalContent: true);
        acc.NextLine();
        foreach (var (word, startMs, endMs) in words)
            acc.AddWord(word, Text.Normalise(word), startMs, endMs);
        return acc.Build().Text;
    }

    /// <summary>Builds a single-canvas temporal Text with multiple cues (lines).</summary>
    private static Text BuildTemporalMultiLine(
        string canvasId,
        IEnumerable<(string Cue, int StartMs, int EndMs)> cues)
    {
        var acc = new TextAccumulator();
        acc.BeginPage(canvasId, isTemporalContent: true);
        foreach (var (cue, startMs, endMs) in cues)
        {
            acc.NextLine();
            foreach (var word in cue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                acc.AddWord(word, Text.Normalise(word), startMs, endMs);
        }
        return acc.Build().Text;
    }

    private sealed class StubTextCache(Text? text) : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult(text);
        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult<AutoComplete?>(null);
    }
}
