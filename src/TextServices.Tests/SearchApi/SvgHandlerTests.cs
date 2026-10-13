using System.Xml.Linq;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Search.Api.Features.Svg;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

/// <summary>
/// Tests for <see cref="SvgHandler"/> and <see cref="SvgBuilder"/>.
/// </summary>
public class SvgHandlerTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    // -------------------------------------------------------------------------
    // 404 cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TextNotFound_ReturnsNull()
    {
        var handler = MakeHandler(text: null);
        var result = await handler.Handle(new SvgRequest("missing", 0), default);
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task Handle_CanvasIndexOutOfRange_ReturnsNull(int index)
    {
        var handler = MakeHandler(BuildText(1000, 1500, ["hello world"]));
        var result = await handler.Handle(new SvgRequest("test/book", index), default);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_TemporalCanvas_ReturnsNull()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1", isTemporalContent: true);
        acc.NextLine();
        acc.AddWord("hello", "hello", 0, 1000);
        var handler = MakeHandler(acc.Build().Text);

        var result = await handler.Handle(new SvgRequest("test/book", 0), default);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NoDimensionsAndNoManifest_ReturnsNull()
    {
        var handler = MakeHandler(BuildText(0, 0, ["hello world"]));
        var result = await handler.Handle(new SvgRequest("test/book", 0), default);
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Response shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ViewBoxUsesCanvasDimensions()
    {
        var handler = MakeHandler(BuildText(1000, 1500, ["hello world"]));

        var svg = await Parse(handler, 0);

        svg.Name.ShouldBe(Svg + "svg");
        svg.Attribute("viewBox")!.Value.ShouldBe("0 0 1000 1500");
    }

    [Fact]
    public async Task Handle_NoDimensionsOnImage_FallsBackToManifest()
    {
        const string manifest =
            """{"type":"Manifest","items":[{"id":"https://example.org/c/1","type":"Canvas","width":800,"height":1200}]}""";
        var handler = MakeHandler(BuildText(0, 0, ["hello world"]), manifest);

        var svg = await Parse(handler, 0);

        svg.Attribute("viewBox")!.Value.ShouldBe("0 0 800 1200");
    }

    [Fact]
    public async Task Handle_OneTextElementPerLine()
    {
        var handler = MakeHandler(BuildText(1000, 1500, ["line one", "line two", "line three"]));

        var svg = await Parse(handler, 0);

        var texts = svg.Elements(Svg + "text").ToList();
        texts.Select(t => t.Value).ShouldBe(["line one", "line two", "line three"]);
    }

    [Fact]
    public async Task Handle_TextElementPositionedFromLineBoundingBox()
    {
        // Words at x=10,y=20,w=50,h=30 and x=70,y=22,w=60,h=28
        // Outer box: x=10, y=20, w=120, h=30 → baseline y = 20 + 30 * 0.75 = 42
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1", width: 1000, height: 1500);
        acc.NextLine();
        acc.AddWord("hello", "hello", 10, 20, 50, 30);
        acc.AddWord("world", "world", 70, 22, 60, 28);
        var handler = MakeHandler(acc.Build().Text);

        var svg = await Parse(handler, 0);

        var text = svg.Element(Svg + "text")!;
        text.Attribute("x")!.Value.ShouldBe("10");
        text.Attribute("y")!.Value.ShouldBe("42");
        text.Attribute("textLength")!.Value.ShouldBe("120");
        text.Attribute("font-size")!.Value.ShouldBe("30");
        text.Attribute("lengthAdjust")!.Value.ShouldBe("spacingAndGlyphs");
        text.Attribute("class")!.Value.ShouldBe("text-line-segment");
    }

    [Fact]
    public async Task Handle_IncludesInvisibleTextStyle()
    {
        var handler = MakeHandler(BuildText(1000, 1500, ["hello"]));

        var svg = await Parse(handler, 0);

        svg.Element(Svg + "style")!.Value.ShouldContain(".text-line-segment");
    }

    [Fact]
    public async Task Handle_SecondCanvas_OnlyContainsThatCanvasText()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1", width: 1000, height: 1500);
        acc.NextLine();
        acc.AddWord("first", "first", 0, 0, 50, 20);
        acc.BeginPage("https://example.org/c/2", width: 1000, height: 1500);
        acc.NextLine();
        acc.AddWord("second", "second", 0, 0, 50, 20);
        var handler = MakeHandler(acc.Build().Text);

        var svg = await Parse(handler, 1);

        svg.Elements(Svg + "text").Select(t => t.Value).ShouldBe(["second"]);
    }

    [Fact]
    public async Task Handle_TextIsXmlEscaped()
    {
        var handler = MakeHandler(BuildText(1000, 1500, ["a<b&c"]));

        var svg = await Parse(handler, 0);

        svg.Element(Svg + "text")!.Value.ShouldBe("a<b&c");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<XElement> Parse(SvgHandler handler, int canvasIndex)
    {
        var result = await handler.Handle(new SvgRequest("test/book", canvasIndex), default);
        result.ShouldNotBeNull();
        return XElement.Parse(result);
    }

    private static SvgHandler MakeHandler(Text? text, string? manifestJson = null)
        => new(new StubTextStore(manifestJson), new StubTextCache(text));

    /// <summary>Builds a single-canvas Text with one line per entry in <paramref name="lines"/>.</summary>
    private static Text BuildText(int width, int height, IEnumerable<string> lines)
    {
        var acc = new TextAccumulator();
        acc.BeginPage("https://example.org/c/1", width: width, height: height);
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

    private sealed class StubTextStore(string? manifestJson) : ITextStore
    {
        public Task<string?> LoadManifest(string key) => Task.FromResult(manifestJson);
        public Task SaveManifest(string key, string json) => Task.CompletedTask;
        public Task SaveText(string key, Text text) => Task.CompletedTask;
        public Task<Text?> LoadText(string key) => Task.FromResult<Text?>(null);
        public Task SaveAutoComplete(string key, AutoComplete ac) => Task.CompletedTask;
        public Task<AutoComplete?> LoadAutoComplete(string key) => Task.FromResult<AutoComplete?>(null);
        public Task SaveRawText(string key, string raw) => Task.CompletedTask;
        public Task<string?> LoadRawText(string key) => Task.FromResult<string?>(null);
        public Task SavePdf(string key, Stream s) => Task.CompletedTask;
        public Task<Stream?> LoadPdf(string key) => Task.FromResult<Stream?>(null);
        public Task SaveFigures(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadFigures(string key) => Task.FromResult<string?>(null);
        public Task SaveAnnotations(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadAnnotations(string key) => Task.FromResult<string?>(null);
        public Task<bool> Exists(string key) => Task.FromResult(false);
        public Task SaveCapabilities(string key, int services) => Task.CompletedTask;
        public Task<int?> LoadCapabilities(string key) => Task.FromResult<int?>(null);
        public Task SavePageSequence(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadPageSequence(string key) => Task.FromResult<string?>(null);
        public Task DeleteArtefacts(string key) => Task.CompletedTask;
    }
}
