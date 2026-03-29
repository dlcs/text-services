using System.Xml.Linq;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Tests for AltoTextFormatProvider and TextBuilder using inline ALTO XML.
/// No external files required — all ALTO snippets are constructed in-test.
/// </summary>
public class AltoParsingTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const string NsV2 = "http://www.loc.gov/standards/alto/ns-v2#";
    private const string NsV3 = "http://www.loc.gov/standards/alto/ns-v3#";

    /// <summary>
    /// Builds a minimal ALTO XElement with a single TextLine containing the
    /// given String elements.  Pass an empty <paramref name="ns"/> for no-namespace ALTO.
    /// </summary>
    private static XElement MakeAlto(
        string ns,
        int pageWidth, int pageHeight,
        string stringsXml)
    {
        var xmlns = string.IsNullOrEmpty(ns) ? string.Empty : $" xmlns=\"{ns}\"";
        return XElement.Parse($"""
            <alto{xmlns}>
              <Layout>
                <Page WIDTH="{pageWidth}" HEIGHT="{pageHeight}">
                  <PrintSpace>
                    <TextBlock>
                      <TextLine>
                        {stringsXml}
                      </TextLine>
                    </TextBlock>
                  </PrintSpace>
                </Page>
              </Layout>
            </alto>
            """);
    }

    /// <summary>
    /// Builds a TextBuildResult from a single ALTO page, with the canvas
    /// at the same dimensions as the ALTO page (no rescaling).
    /// </summary>
    private static TextBuildResult BuildSinglePage(
        string altoXml,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var root = XElement.Parse(altoXml);
        int w = canvasWidth  > 0 ? canvasWidth  : (int?)root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Page")?.Attribute("WIDTH") ?? 1000;
        int h = canvasHeight > 0 ? canvasHeight : (int?)root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Page")?.Attribute("HEIGHT") ?? 1000;

        var builder = new TextBuilder();
        builder.AddPage("https://example.org/canvas/1", w, h, root,
            profile: NsV2);
        return builder.Build();
    }

    /// <summary>Shorthand: build result and return the Text object.</summary>
    private static Text ParseAlto(XElement root, int canvasWidth, int canvasHeight)
    {
        var builder = new TextBuilder();
        builder.AddPage("https://example.org/canvas/1", canvasWidth, canvasHeight,
            root, profile: NsV2);
        return builder.Build().Text;
    }

    // -------------------------------------------------------------------------
    // Supports()
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("http://www.loc.gov/standards/alto/ns-v3#", null)]
    [InlineData("http://www.loc.gov/standards/alto/ns-v2#", null)]
    [InlineData("http://schema.ccs-gmbh.com/ALTO", null)]
    [InlineData(null, "ALTO")]
    [InlineData(null, "METS-ALTO")]
    [InlineData(null, "METS-ALTO XML")]
    [InlineData("some/alto/profile", null)]
    public void Supports_AltoProfileOrLabel_ReturnsTrue(string? profile, string? label)
    {
        var provider = new AltoTextFormatProvider();
        provider.Supports(profile, label).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("application/json", null)]
    [InlineData("http://iiif.io/api/presentation/3", null)]
    [InlineData(null, "hOCR")]
    public void Supports_NonAltoProfileOrLabel_ReturnsFalse(string? profile, string? label)
    {
        var provider = new AltoTextFormatProvider();
        provider.Supports(profile, label).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Namespace detection
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(NsV2)]
    [InlineData(NsV3)]
    [InlineData("")]   // no namespace
    public void AddPage_AllNamespaceVariants_ParsesWords(string ns)
    {
        var alto = MakeAlto(ns, 1000, 2000,
            """<String CONTENT="hello" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>""");

        var text = ParseAlto(alto, 1000, 2000);
        text.Search("hello").Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Basic word parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void AddPage_SingleWord_CorrectContentAndPosition()
    {
        var alto = MakeAlto(NsV2, 1000, 2000,
            """<String CONTENT="parliament" HPOS="100" VPOS="200" WIDTH="80" HEIGHT="25"/>""");

        var text = ParseAlto(alto, 1000, 2000);

        text.Words.Count.ShouldBe(1);
        var word = text.Words.Values.First();
        word.ContentRaw.ShouldBe("parliament");
        word.ContentNorm.ShouldBe("parliament");
        word.X.ShouldBe(100);
        word.Y.ShouldBe(200);
        word.W.ShouldBe(80);
        word.H.ShouldBe(25);
        word.Idx.ShouldBe(0);
    }

    [Fact]
    public void AddPage_MultipleWords_AllIndexed()
    {
        var alto = MakeAlto(NsV2, 1000, 1000, """
            <String CONTENT="the"  HPOS="10" VPOS="10" WIDTH="30" HEIGHT="20"/>
            <String CONTENT="quick" HPOS="50" VPOS="10" WIDTH="50" HEIGHT="20"/>
            <String CONTENT="fox"  HPOS="110" VPOS="10" WIDTH="30" HEIGHT="20"/>
            """);

        var text = ParseAlto(alto, 1000, 1000);
        text.Words.Count.ShouldBe(3);
        text.Search("quick fox").Count.ShouldBe(1);
    }

    [Fact]
    public void AddPage_PunctuationWord_NormalisesToEmpty_Skipped()
    {
        // "---" normalises to empty — should not appear in index
        var alto = MakeAlto(NsV2, 1000, 1000, """
            <String CONTENT="hello" HPOS="0"  VPOS="0" WIDTH="50" HEIGHT="20"/>
            <String CONTENT="---"   HPOS="60" VPOS="0" WIDTH="20" HEIGHT="20"/>
            <String CONTENT="world" HPOS="90" VPOS="0" WIDTH="50" HEIGHT="20"/>
            """);

        var text = ParseAlto(alto, 1000, 1000);
        text.Words.Count.ShouldBe(2);
        text.Search("hello").Count.ShouldBe(1);
        text.Search("world").Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Rescaling
    // -------------------------------------------------------------------------

    [Fact]
    public void AddPage_Rescaling_CoordinatesScaledToCanvas()
    {
        // ALTO recorded at 4000×6000; Canvas is 2000×3000 — half scale
        var alto = MakeAlto(NsV2, 4000, 6000,
            """<String CONTENT="word" HPOS="200" VPOS="400" WIDTH="100" HEIGHT="50"/>""");

        var text = ParseAlto(alto, 2000, 3000);

        var word = text.Words.Values.First();
        word.X.ShouldBe(100);   // 200 * (2000/4000)
        word.Y.ShouldBe(200);   // 400 * (3000/6000)
        word.W.ShouldBe(50);    // 100 * 0.5
        word.H.ShouldBe(25);    // 50  * 0.5
    }

    [Fact]
    public void AddPage_NoRescaling_WhenDimensionsMatch()
    {
        var alto = MakeAlto(NsV2, 1000, 2000,
            """<String CONTENT="word" HPOS="100" VPOS="200" WIDTH="80" HEIGHT="25"/>""");

        var text = ParseAlto(alto, 1000, 2000);

        var word = text.Words.Values.First();
        word.X.ShouldBe(100);
        word.Y.ShouldBe(200);
    }

    // -------------------------------------------------------------------------
    // Space-after (SP elements)
    // -------------------------------------------------------------------------

    [Fact]
    public void AddPage_SpElement_SetsSpaceAfterOnPrecedingWord()
    {
        var alto = MakeAlto(NsV2, 1000, 1000, """
            <String CONTENT="hello" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>
            <SP WIDTH="15"/>
            <String CONTENT="world" HPOS="65" VPOS="0" WIDTH="50" HEIGHT="20"/>
            """);

        var text = ParseAlto(alto, 1000, 1000);
        var hello = text.Words.Values.First(w => w.ContentRaw == "hello");
        hello.Sp.ShouldBe(15);
    }

    // -------------------------------------------------------------------------
    // Multi-line
    // -------------------------------------------------------------------------

    [Fact]
    public void AddPage_TwoTextLines_DifferentLiValues()
    {
        var alto = XElement.Parse($"""
            <alto xmlns="{NsV2}">
              <Layout>
                <Page WIDTH="1000" HEIGHT="2000">
                  <PrintSpace>
                    <TextBlock>
                      <TextLine>
                        <String CONTENT="first" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>
                      </TextLine>
                      <TextLine>
                        <String CONTENT="second" HPOS="0" VPOS="30" WIDTH="60" HEIGHT="20"/>
                      </TextLine>
                    </TextBlock>
                  </PrintSpace>
                </Page>
              </Layout>
            </alto>
            """);

        var text = ParseAlto(alto, 1000, 2000);
        var first  = text.Words.Values.First(w => w.ContentRaw == "first");
        var second = text.Words.Values.First(w => w.ContentRaw == "second");

        first.Li.ShouldNotBe(second.Li);
    }

    // -------------------------------------------------------------------------
    // Hyphenation
    // -------------------------------------------------------------------------

    [Fact]
    public void AddPage_Hyphenation_WithSubsContent_UsesSubsContent()
    {
        // SUBS_CONTENT gives the merged form of the hyphenated word
        var alto = XElement.Parse($"""
            <alto xmlns="{NsV2}">
              <Layout>
                <Page WIDTH="1000" HEIGHT="2000">
                  <PrintSpace>
                    <TextBlock>
                      <TextLine>
                        <String CONTENT="par-" HPOS="0"  VPOS="0" WIDTH="40" HEIGHT="20"
                                SUBS_TYPE="HypPart1" SUBS_CONTENT="parliament"/>
                      </TextLine>
                      <TextLine>
                        <String CONTENT="liament" HPOS="0" VPOS="30" WIDTH="70" HEIGHT="20"
                                SUBS_TYPE="HypPart2" SUBS_CONTENT="parliament"/>
                      </TextLine>
                    </TextBlock>
                  </PrintSpace>
                </Page>
              </Layout>
            </alto>
            """);

        var text = ParseAlto(alto, 1000, 2000);

        // The two ALTO fragments are merged into a single Word at the HypPart1 position.
        // Known limitation (shared with both reference implementations): only the first
        // fragment's bounding box is stored. A search hit on "parliament" will highlight
        // the "par-" line only, not the "liament" continuation. Wellcome's comment:
        // "we'll have to keep it simple and just regard the first part as the full word.
        //  Otherwise a word would have to have two rectangles."
        text.Words.Count.ShouldBe(1);
        text.Words.Values.First().ContentRaw.ShouldBe("parliament");
        text.Words.Values.First().ContentNorm.ShouldBe("parliament");
        text.Search("parliament").Count.ShouldBe(1);
    }

    [Fact]
    public void AddPage_Hyphenation_WithoutSubsContent_ConcatenatesFragments()
    {
        // No SUBS_CONTENT: fragments are concatenated
        var alto = XElement.Parse($"""
            <alto xmlns="{NsV2}">
              <Layout>
                <Page WIDTH="1000" HEIGHT="2000">
                  <PrintSpace>
                    <TextBlock>
                      <TextLine>
                        <String CONTENT="par-" HPOS="0"  VPOS="0" WIDTH="40" HEIGHT="20"
                                SUBS_TYPE="HypPart1"/>
                      </TextLine>
                      <TextLine>
                        <String CONTENT="liament" HPOS="0" VPOS="30" WIDTH="70" HEIGHT="20"
                                SUBS_TYPE="HypPart2"/>
                      </TextLine>
                    </TextBlock>
                  </PrintSpace>
                </Page>
              </Layout>
            </alto>
            """);

        var text = ParseAlto(alto, 1000, 2000);

        // Raw is "par-liament" (concatenated); norm strips the hyphen → "parliament"
        text.Words.Count.ShouldBe(1);
        text.Words.Values.First().ContentNorm.ShouldBe("parliament");
        text.Search("parliament").Count.ShouldBe(1);
    }

    [Fact]
    public void AddPage_Hyphenation_HypPart1Position_UsedForWord()
    {
        // The merged word should use the bounding box of the first (HypPart1) fragment
        var alto = XElement.Parse($"""
            <alto xmlns="{NsV2}">
              <Layout>
                <Page WIDTH="1000" HEIGHT="2000">
                  <PrintSpace>
                    <TextBlock>
                      <TextLine>
                        <String CONTENT="par-" HPOS="100" VPOS="200" WIDTH="40" HEIGHT="20"
                                SUBS_TYPE="HypPart1" SUBS_CONTENT="parliament"/>
                      </TextLine>
                      <TextLine>
                        <String CONTENT="liament" HPOS="0" VPOS="300" WIDTH="70" HEIGHT="20"
                                SUBS_TYPE="HypPart2" SUBS_CONTENT="parliament"/>
                      </TextLine>
                    </TextBlock>
                  </PrintSpace>
                </Page>
              </Layout>
            </alto>
            """);

        var text = ParseAlto(alto, 1000, 2000);

        var word = text.Words.Values.First();
        word.X.ShouldBe(100);  // from HypPart1
        word.Y.ShouldBe(200);  // from HypPart1
    }

    [Fact]
    public void AddPage_Hyphenation_MarkerCharacter_AlsoDetected()
    {
        // '¬' at end of word is an alternative hyphen marker used by some ALTO producers
        var alto = XElement.Parse($"""
            <alto xmlns="{NsV2}">
              <Layout>
                <Page WIDTH="1000" HEIGHT="2000">
                  <PrintSpace>
                    <TextBlock>
                      <TextLine>
                        <String CONTENT="par¬" HPOS="0" VPOS="0" WIDTH="40" HEIGHT="20"/>
                      </TextLine>
                      <TextLine>
                        <String CONTENT="liament" HPOS="0" VPOS="30" WIDTH="70" HEIGHT="20"
                                SUBS_CONTENT="parliament"/>
                      </TextLine>
                    </TextBlock>
                  </PrintSpace>
                </Page>
              </Layout>
            </alto>
            """);

        var text = ParseAlto(alto, 1000, 2000);

        text.Words.Count.ShouldBe(1);
        text.Words.Values.First().ContentNorm.ShouldBe("parliament");
    }

    // -------------------------------------------------------------------------
    // ComposedBlocks
    // -------------------------------------------------------------------------

    [Fact]
    public void AddPage_ComposedBlock_DetectedWithCorrectBounds()
    {
        var alto = XElement.Parse($"""
            <alto xmlns="{NsV2}">
              <Layout>
                <Page WIDTH="1000" HEIGHT="2000">
                  <PrintSpace>
                    <ComposedBlock TYPE="Table" ID="CB1"
                                   HPOS="50" VPOS="100" WIDTH="400" HEIGHT="200">
                      <TextBlock>
                        <TextLine>
                          <String CONTENT="alpha" HPOS="60" VPOS="110" WIDTH="50" HEIGHT="20"/>
                          <String CONTENT="beta"  HPOS="120" VPOS="110" WIDTH="40" HEIGHT="20"/>
                        </TextLine>
                      </TextBlock>
                    </ComposedBlock>
                    <TextBlock>
                      <TextLine>
                        <String CONTENT="outside" HPOS="0" VPOS="350" WIDTH="70" HEIGHT="20"/>
                      </TextLine>
                    </TextBlock>
                  </PrintSpace>
                </Page>
              </Layout>
            </alto>
            """);

        var result = ParseAlto(alto, 1000, 2000);

        result.ComposedBlocks.Length.ShouldBe(1);
        var cb = result.ComposedBlocks[0];
        cb.BlockType.ShouldBe("Table");
        cb.X.ShouldBe(50);
        cb.Y.ShouldBe(100);
        cb.W.ShouldBe(400);
        cb.H.ShouldBe(200);

        // StartCharacter = PosNorm of "alpha"; EndCharacter = PosNorm of "beta"
        result.Words[cb.StartCharacter].ContentNorm.ShouldBe("alpha");
        result.Words[cb.EndCharacter].ContentNorm.ShouldBe("beta");
    }

    [Fact]
    public void AddPage_NoComposedBlocks_CollectionEmpty()
    {
        var alto = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="hello" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>""");

        var text = ParseAlto(alto, 1000, 1000);
        text.ComposedBlocks.Length.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // TextBuilder — multi-page
    // -------------------------------------------------------------------------

    [Fact]
    public void TextBuilder_TwoPages_BothPagesIndexed()
    {
        var page1 = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="fox" HPOS="0" VPOS="0" WIDTH="30" HEIGHT="20"/>""");
        var page2 = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="jumps" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>""");

        var builder = new TextBuilder();
        builder.AddPage("https://example.org/canvas/1", 1000, 1000, page1, profile: NsV2);
        builder.AddPage("https://example.org/canvas/2", 1000, 1000, page2, profile: NsV2);
        var text = builder.Build().Text;

        text.Images.Length.ShouldBe(2);
        text.Search("fox").Count.ShouldBe(1);
        text.Search("jumps").Count.ShouldBe(1);
    }

    [Fact]
    public void TextBuilder_TwoPages_PageBoundaryWordsNotCoalesced()
    {
        // "fox" ends page 1; "jumps" starts page 2 — different Li → not coalesced
        var page1 = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="fox" HPOS="0" VPOS="0" WIDTH="30" HEIGHT="20"/>""");
        var page2 = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="jumps" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>""");

        var builder = new TextBuilder();
        builder.AddPage("https://example.org/canvas/1", 1000, 1000, page1, profile: NsV2);
        builder.AddPage("https://example.org/canvas/2", 1000, 1000, page2, profile: NsV2);
        var text = builder.Build().Text;

        var results = text.Search("fox jumps");
        results.Count.ShouldBe(2);
        results[0].Idx.ShouldBe(0);
        results[1].Idx.ShouldBe(1);
    }

    [Fact]
    public void TextBuilder_SparseManifest_NullPageSilentlySkipped()
    {
        var page1 = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="hello" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>""");

        var builder = new TextBuilder();
        builder.AddPage("https://example.org/canvas/1", 1000, 1000, page1, profile: NsV2);
        builder.AddPage("https://example.org/canvas/2", 1000, 1000, null,  profile: NsV2); // sparse
        var result = builder.Build();

        result.IsEmpty.ShouldBeFalse();
        result.Text.Words.Count.ShouldBe(1);
        // Only page 1 contributed an Image entry (page 2 was null/skipped)
        result.Text.Images.Length.ShouldBe(1);
    }

    [Fact]
    public void TextBuilder_NoPages_ReturnsEmpty()
    {
        var result = new TextBuilder().Build();
        result.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TextBuilder_UnknownFormat_PageSkipped()
    {
        var page = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="hello" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>""");

        var builder = new TextBuilder();
        // Pass a profile that no provider supports
        builder.AddPage("https://example.org/canvas/1", 1000, 1000, page,
            profile: "application/json");
        builder.Build().IsEmpty.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // AutoComplete
    // -------------------------------------------------------------------------

    [Fact]
    public void TextBuilder_AutoComplete_ContainsIndexedWords()
    {
        var alto = MakeAlto(NsV2, 1000, 1000, """
            <String CONTENT="parliament" HPOS="0" VPOS="0" WIDTH="80" HEIGHT="20"/>
            <String CONTENT="met"        HPOS="90" VPOS="0" WIDTH="30" HEIGHT="20"/>
            """);

        var builder = new TextBuilder();
        builder.AddPage("https://example.org/canvas/1", 1000, 1000, alto, profile: NsV2);
        var ac = builder.Build().AutoComplete;

        ac.GetSuggestions("par").ShouldContain("parliament");
        // "met" is only 3 chars — in AutoComplete only words > 2 chars are included,
        // but "met" is exactly 3 — verify it is included
        ac.GetSuggestions("met").ShouldContain("met");
    }

    // -------------------------------------------------------------------------
    // Image boundary (StartCharacter)
    // -------------------------------------------------------------------------

    [Fact]
    public void TextBuilder_TwoPages_ImageStartCharactersCorrect()
    {
        var page1 = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="alpha" HPOS="0" VPOS="0" WIDTH="50" HEIGHT="20"/>""");
        var page2 = MakeAlto(NsV2, 1000, 1000,
            """<String CONTENT="beta" HPOS="0" VPOS="0" WIDTH="40" HEIGHT="20"/>""");

        var builder = new TextBuilder();
        builder.AddPage("https://example.org/canvas/1", 1000, 1000, page1, profile: NsV2);
        builder.AddPage("https://example.org/canvas/2", 1000, 1000, page2, profile: NsV2);
        var text = builder.Build().Text;

        // Page 1 starts at 0; page 2 starts after "alpha "
        text.Images[0].StartCharacter.ShouldBe(0);
        text.Images[1].StartCharacter.ShouldBeGreaterThan(0);
        text.Images[0].ImageIdentifier.ShouldBe("https://example.org/canvas/1");
        text.Images[1].ImageIdentifier.ShouldBe("https://example.org/canvas/2");
    }
}
