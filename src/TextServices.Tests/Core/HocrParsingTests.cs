using System.Xml.Linq;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Tests for HocrTextFormatProvider and TextBuilder using inline hOCR HTML.
/// All hOCR snippets are constructed in-test; no external files are required.
/// </summary>
public class HocrParsingTests
{
    private readonly HocrTextFormatProvider _provider = new();

    // -------------------------------------------------------------------------
    // Supports()
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("text/vnd.hocr+html", null)]
    [InlineData("application/hocr+html", null)]
    [InlineData("https://example.org/formats/hocr", null)]
    [InlineData(null, "hOCR output")]
    [InlineData(null, "Tesseract hocr")]
    public void Supports_KnownHocrProfiles_ReturnsTrue(string? profile, string? label)
        => _provider.Supports(profile, label).ShouldBeTrue();

    [Theory]
    [InlineData("http://www.loc.gov/standards/alto/v3/alto.xsd", null)]
    [InlineData(null, "METS-ALTO XML")]
    [InlineData(null, null)]
    public void Supports_NonHocrProfiles_ReturnsFalse(string? profile, string? label)
        => _provider.Supports(profile, label).ShouldBeFalse();

    // -------------------------------------------------------------------------
    // Basic word extraction
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_BasicWords_ExtractsNormalisedText()
    {
        var result = Build(HocrPage(1000, 1000,
            HocrLine("bbox 0 0 500 30",
                HocrWord("bbox 0 0 100 30", "Hello"),
                HocrWord("bbox 110 0 200 30", "World"))));

        result.Text.NormalisedFullText.ShouldBe("hello world");
    }

    [Fact]
    public void ProcessPage_BasicWords_CorrectBoundingBoxes()
    {
        var result = Build(HocrPage(1000, 1000,
            HocrLine("bbox 0 50 500 80",
                HocrWord("bbox 10 50 110 80", "Test"))));

        var word = result.Text.Words.Values.Single();
        word.X.ShouldBe(10);
        word.Y.ShouldBe(50);
        word.W.ShouldBe(100);   // x1 - x0 = 110 - 10
        word.H.ShouldBe(30);    // y1 - y0 = 80 - 50
    }

    // -------------------------------------------------------------------------
    // Coordinate scaling
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_HocrSmallerThanCanvas_ScalesCoordinates()
    {
        // hOCR page 500×500, canvas 1000×1000 → scale 2×
        var result = Build(HocrPage(500, 500,
            HocrLine("bbox 0 0 500 50",
                HocrWord("bbox 10 10 60 40", "word"))),
            canvasWidth: 1000, canvasHeight: 1000);

        var word = result.Text.Words.Values.Single();
        word.X.ShouldBe(20);   // 10 * 2
        word.Y.ShouldBe(20);   // 10 * 2
        word.W.ShouldBe(100);  // (60 - 10) * 2
        word.H.ShouldBe(60);   // (40 - 10) * 2
    }

    [Fact]
    public void ProcessPage_NoBboxOnPage_FallsBackToCanvasDimensions()
    {
        // No ocr_page bbox → no scaling (treat page coords as canvas coords)
        var html = XElement.Parse("""
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body>
                <div class="ocr_page">
                  <span class="ocr_line" title="bbox 0 0 200 30">
                    <span class="ocrx_word" title="bbox 5 5 55 25">word</span>
                  </span>
                </div>
              </body>
            </html>
            """);

        var result = BuildFromElement(html, 200, 30);
        var word = result.Text.Words.Values.Single();
        word.X.ShouldBe(5);
        word.W.ShouldBe(50);
    }

    // -------------------------------------------------------------------------
    // Tesseract ocrx_word vs ocr_word
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_OcrxWord_Tesseract_Extracted()
    {
        var result = Build(HocrPage(1000, 100,
            HocrLine("bbox 0 0 1000 100",
                """<span class="ocrx_word" title="bbox 0 0 200 100">tesseract</span>""")));

        result.Text.NormalisedFullText.ShouldBe("tesseract");
    }

    [Fact]
    public void ProcessPage_OcrWord_FallbackExtracted()
    {
        var html = XElement.Parse("""
            <html>
              <body>
                <div class="ocr_page" title="bbox 0 0 1000 1000">
                  <span class="ocr_line" title="bbox 0 0 500 30">
                    <span class="ocr_word" title="bbox 0 0 100 30">standard</span>
                  </span>
                </div>
              </body>
            </html>
            """);
        var result = BuildFromElement(html, 1000, 1000);
        result.Text.NormalisedFullText.ShouldBe("standard");
    }

    // -------------------------------------------------------------------------
    // Non-text blocks
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_OcrFigure_AddedAsIllustrationBlock()
    {
        var result = Build(HocrPage(1000, 1000,
            """<div class="ocr_figure" title="bbox 100 200 400 600"></div>"""));

        result.Text.ComposedBlocks.ShouldHaveSingleItem();
        var cb = result.Text.ComposedBlocks[0];
        cb.BlockType.ShouldBe("Illustration");
        cb.X.ShouldBe(100);
        cb.Y.ShouldBe(200);
        cb.W.ShouldBe(300);   // 400 - 100
        cb.H.ShouldBe(400);   // 600 - 200
    }

    [Fact]
    public void ProcessPage_OcrTable_AddedAsTableBlock()
    {
        var result = Build(HocrPage(1000, 1000,
            """<div class="ocr_table" title="bbox 50 50 300 200"></div>"""));

        result.Text.ComposedBlocks.ShouldHaveSingleItem();
        result.Text.ComposedBlocks[0].BlockType.ShouldBe("Table");
    }

    [Theory]
    [InlineData("ocr_graphic")]
    [InlineData("ocr_linedrawing")]
    [InlineData("ocr_photo")]
    public void ProcessPage_OtherFigureClasses_AddedAsIllustration(string className)
    {
        var result = Build(HocrPage(1000, 1000,
            $"""<div class="{className}" title="bbox 10 10 200 300"></div>"""));

        result.Text.ComposedBlocks.ShouldHaveSingleItem();
        result.Text.ComposedBlocks[0].BlockType.ShouldBe("Illustration");
    }

    [Fact]
    public void ProcessPage_ZeroAreaBlock_Skipped()
    {
        var result = Build(HocrPage(1000, 1000,
            """<div class="ocr_figure" title="bbox 100 100 100 100"></div>"""));

        result.Text.ComposedBlocks.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // XHTML namespace
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_XhtmlNamespace_WordsExtracted()
    {
        var html = XElement.Parse("""
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body>
                <div class="ocr_page" title="bbox 0 0 2000 3000">
                  <span class="ocr_line" title="bbox 0 0 500 40">
                    <span class="ocrx_word" title="bbox 0 0 200 40">xhtml</span>
                    <span class="ocrx_word" title="bbox 210 0 400 40">namespace</span>
                  </span>
                </div>
              </body>
            </html>
            """);
        var result = BuildFromElement(html, 2000, 3000);
        result.Text.NormalisedFullText.ShouldBe("xhtml namespace");
    }

    // -------------------------------------------------------------------------
    // Multi-word autocomplete
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_Words_AutoCompletePopulated()
    {
        var result = Build(HocrPage(1000, 1000,
            HocrLine("bbox 0 0 1000 30",
                HocrWord("bbox 0 0 100 30", "Catalogue"),
                HocrWord("bbox 110 0 200 30", "of"))));

        result.AutoComplete.Buckets.ShouldContainKey("cat");
        result.AutoComplete.Buckets["cat"].ShouldContain("catalogue");
    }

    // -------------------------------------------------------------------------
    // TextBuilder integration — provider selected by profile
    // -------------------------------------------------------------------------

    [Fact]
    public void TextBuilder_HocrProfile_UsesHocrProvider()
    {
        var tb = new TextBuilder();
        var html = XElement.Parse(HocrPage(500, 500,
            HocrLine("bbox 0 0 500 30",
                HocrWord("bbox 0 0 100 30", "integrated"))));

        tb.AddPage("https://example.org/canvas/1", 500, 500, html,
            profile: "text/vnd.hocr+html");

        var result = tb.Build();
        result.Text.NormalisedFullText.ShouldBe("integrated");
    }

    [Fact]
    public void TextBuilder_HocrLabel_UsesHocrProvider()
    {
        var tb = new TextBuilder();
        var html = XElement.Parse(HocrPage(500, 500,
            HocrLine("bbox 0 0 500 30",
                HocrWord("bbox 0 0 100 30", "bylabel"))));

        tb.AddPage("https://example.org/canvas/1", 500, 500, html,
            label: "Tesseract hOCR output");

        var result = tb.Build();
        result.Text.NormalisedFullText.ShouldBe("bylabel");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private TextBuildResult Build(string hocrPageXml,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var root = XElement.Parse(hocrPageXml);
        // Read page dimensions from the bbox if not overridden
        if (canvasWidth == 0 || canvasHeight == 0)
        {
            var bbox = root.Attribute("title")?.Value ?? string.Empty;
            var parts = bbox.Replace("bbox ", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                canvasWidth  = canvasWidth  == 0 ? int.Parse(parts[2]) : canvasWidth;
                canvasHeight = canvasHeight == 0 ? int.Parse(parts[3]) : canvasHeight;
            }
            canvasWidth  = canvasWidth  == 0 ? 1000 : canvasWidth;
            canvasHeight = canvasHeight == 0 ? 1000 : canvasHeight;
        }
        return BuildFromElement(root, canvasWidth, canvasHeight);
    }

    private TextBuildResult BuildFromElement(XElement root, int canvasWidth, int canvasHeight)
    {
        var accumulator = new TextAccumulator();
        _provider.ProcessPage(accumulator, root, "https://example.org/canvas/1",
            canvasWidth, canvasHeight);
        return accumulator.Build();
    }

    /// <summary>Builds a minimal hOCR ocr_page element (not a full HTML document).</summary>
    private static string HocrPage(int pageWidth, int pageHeight, params string[] bodyContent)
        => $"""<div class="ocr_page" title="bbox 0 0 {pageWidth} {pageHeight}">{string.Concat(bodyContent)}</div>""";

    private static string HocrLine(string bbox, params string[] wordContent)
        => $"""<span class="ocr_line" title="{bbox}">{string.Concat(wordContent)}</span>""";

    private static string HocrWord(string bbox, string text)
        => $"""<span class="ocrx_word" title="{bbox}">{text}</span>""";
}
