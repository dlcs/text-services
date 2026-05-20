using Shouldly;
using TextServices.Builder.Api.Services;
using TextServices.Core.Providers;

namespace TextServices.Tests.Core;

/// <summary>
/// Unit tests for <see cref="W3cAnnotationTextFormatProvider"/>.
/// </summary>
public class W3cAnnotationParsingTests
{
    private static readonly W3cAnnotationTextFormatProvider Provider = new();

    // -------------------------------------------------------------------------
    // Supports()
    // -------------------------------------------------------------------------

    [Fact]
    public void Supports_FormatSentinel_ReturnsTrue()
        => Provider.Supports(null, W3cAnnotationTextFormatProvider.FormatSentinel, null).ShouldBeTrue();

    [Fact]
    public void Supports_NullFormat_ReturnsFalse()
        => Provider.Supports(null, null, null).ShouldBeFalse();

    [Fact]
    public void Supports_OtherFormat_ReturnsFalse()
        => Provider.Supports(null, "text/vtt", null).ShouldBeFalse();

    // -------------------------------------------------------------------------
    // URI fragment targets (#xywh=)
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_SpatialAnnotation_UriFragment_IndexesWords()
    {
        var json = AnnotationPage([
            Annotation("Hello World", "canvas1#xywh=10,20,200,30"),
        ]);

        var (text, _) = Build(json, "canvas1");

        text.Words.Count.ShouldBe(2);
        var words = text.Words.Values.OrderBy(w => w.Wd).ToList();
        words[0].ContentNorm.ShouldBe("hello");
        words[0].X.ShouldBe(10); words[0].Y.ShouldBe(20);
        words[0].W.ShouldBe(200); words[0].H.ShouldBe(30);
        words[1].ContentNorm.ShouldBe("world");
        words[1].X.ShouldBe(10); // same bounding box
    }

    [Fact]
    public void ProcessPage_SpatialAnnotation_IsTemporalContent_False()
    {
        var json = AnnotationPage([Annotation("Text", "canvas1#xywh=0,0,100,20")]);
        var (text, _) = Build(json, "canvas1");
        text.Images[0].IsTemporalContent.ShouldBeFalse();
    }

    [Fact]
    public void ProcessPage_MultipleAnnotations_DifferentLiValues()
    {
        var json = AnnotationPage([
            Annotation("Line one",  "canvas1#xywh=0,0,100,20"),
            Annotation("Line two",  "canvas1#xywh=0,30,100,20"),
        ]);

        var (text, _) = Build(json, "canvas1");
        var words = text.Words.Values.OrderBy(w => w.Wd).ToList();

        words[0].Li.ShouldNotBe(words[2].Li); // "line" (first) vs "line" (second)
    }

    [Fact]
    public void ProcessPage_WordsWithinAnnotation_SameLi()
    {
        var json = AnnotationPage([Annotation("prime minister spoke", "canvas1#xywh=0,0,400,20")]);

        var (text, _) = Build(json, "canvas1");
        var words = text.Words.Values.OrderBy(w => w.Wd).ToList();

        words[0].Li.ShouldBe(words[1].Li);
        words[1].Li.ShouldBe(words[2].Li);
    }

    [Fact]
    public void ProcessPage_AllWordsShareAnnotationBoundingBox()
    {
        var json = AnnotationPage([Annotation("alpha beta gamma", "canvas1#xywh=5,10,300,40")]);

        var (text, _) = Build(json, "canvas1");

        foreach (var word in text.Words.Values)
        {
            word.X.ShouldBe(5);
            word.Y.ShouldBe(10);
            word.W.ShouldBe(300);
            word.H.ShouldBe(40);
        }
    }

    [Fact]
    public void ProcessPage_NormalisedText_BuiltCorrectly()
    {
        var json = AnnotationPage([Annotation("Hello, World!", "canvas1#xywh=0,0,100,20")]);

        var (text, _) = Build(json, "canvas1");

        text.NormalisedFullText.ShouldContain("hello");
        text.NormalisedFullText.ShouldContain("world");
    }

    [Fact]
    public void ProcessPage_NonSupplementingAnnotation_Skipped()
    {
        var json = AnnotationPage([Annotation("Ignored", "canvas1#xywh=0,0,100,20", motivation: "painting")]);

        var (text, _) = Build(json, "canvas1");
        text.Words.ShouldBeEmpty();
    }

    [Fact]
    public void ProcessPage_NoItems_ReturnsEmpty()
    {
        var json = """{"type":"AnnotationPage"}""";
        var acc = new TextAccumulator();
        Provider.ProcessPage(acc, json, "canvas1", 1000, 1000);
        acc.Build().IsEmpty.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // SpecificResource / FragmentSelector targets
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_SpecificResource_FragmentSelector_Spatial()
    {
        var json = AnnotationPage([
            AnnotationSpecificResource("Hello World", "canvas1", "xywh=50,60,150,25"),
        ]);

        var (text, _) = Build(json, "canvas1");

        text.Words.Count.ShouldBe(2);
        var first = text.Words.Values.First();
        first.X.ShouldBe(50); first.Y.ShouldBe(60); first.W.ShouldBe(150); first.H.ShouldBe(25);
    }

    [Fact]
    public void ProcessPage_SpecificResource_PixelPrefix_Spatial()
    {
        var json = AnnotationPage([
            AnnotationSpecificResource("word", "canvas1", "xywh=pixel:10,20,30,40"),
        ]);

        var (text, _) = Build(json, "canvas1");

        var word = text.Words.Values.Single();
        word.X.ShouldBe(10); word.Y.ShouldBe(20); word.W.ShouldBe(30); word.H.ShouldBe(40);
    }

    // -------------------------------------------------------------------------
    // Temporal targets (#t=)
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessPage_TemporalAnnotation_UriFragment_IndexesWords()
    {
        var json = AnnotationPage([Annotation("spoken words", "canvas1#t=5.0,8.5")]);

        var (text, _) = Build(json, "canvas1");

        text.Words.Count.ShouldBe(2);
        var words = text.Words.Values.OrderBy(w => w.Wd).ToList();
        words[0].StartMs.ShouldBe(5000);
        words[0].EndMs.ShouldBe(8500);
        words[1].StartMs.ShouldBe(5000);
    }

    [Fact]
    public void ProcessPage_TemporalAnnotation_IsTemporalContent_True()
    {
        var json = AnnotationPage([Annotation("spoken", "canvas1#t=0,5")]);
        var (text, _) = Build(json, "canvas1");
        text.Images[0].IsTemporalContent.ShouldBeTrue();
    }

    [Fact]
    public void ProcessPage_TemporalAnnotation_SpatialCoordsAreZero()
    {
        var json = AnnotationPage([Annotation("spoken", "canvas1#t=0,5")]);
        var (text, _) = Build(json, "canvas1");
        var word = text.Words.Values.Single();
        word.X.ShouldBe(0); word.Y.ShouldBe(0); word.W.ShouldBe(0); word.H.ShouldBe(0);
    }

    [Fact]
    public void ProcessPage_TemporalSpecificResource()
    {
        var json = AnnotationPage([
            AnnotationSpecificResource("hello", "canvas1", "t=10.0,15.5"),
        ]);

        var (text, _) = Build(json, "canvas1");

        var word = text.Words.Values.Single();
        word.StartMs.ShouldBe(10000);
        word.EndMs.ShouldBe(15500);
    }

    // -------------------------------------------------------------------------
    // Search integration
    // -------------------------------------------------------------------------

    [Fact]
    public void Search_SingleWord_ReturnsAnnotationBoundingBox()
    {
        var json = AnnotationPage([
            Annotation("REPORT OF THE COMMITTEE", "canvas1#xywh=490,502,2376,100"),
        ]);

        var (text, _) = Build(json, "canvas1");
        var rects = text.Search("committee");

        rects.ShouldHaveSingleItem();
        var rect = rects[0];
        rect.X.ShouldBe(490); rect.Y.ShouldBe(502);
        rect.W.ShouldBe(2376); rect.H.ShouldBe(100);
    }

    [Fact]
    public void Search_PhraseWithinAnnotation_ReturnsSingleRect()
    {
        var json = AnnotationPage([
            Annotation("prime minister spoke today", "canvas1#xywh=0,0,500,30"),
        ]);

        var (text, _) = Build(json, "canvas1");
        var rects = text.Search("minister spoke");

        // All words share the same box — coalesces to one rect.
        rects.ShouldHaveSingleItem();
    }

    [Fact]
    public void Search_PhraseSpanningTwoAnnotations_ReturnsTwoRects()
    {
        var json = AnnotationPage([
            Annotation("end of line one",   "canvas1#xywh=0,0,400,20"),
            Annotation("start of line two", "canvas1#xywh=0,30,400,20"),
        ]);

        var (text, _) = Build(json, "canvas1");
        var rects = text.Search("one start");

        rects.Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // ManifestReducer integration
    // -------------------------------------------------------------------------

    [Fact]
    public void ManifestReducer_ExternalAnnotationPage_Detected()
    {
        var manifest = $$"""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "id": "https://example.org/manifest",
          "type": "Manifest",
          "items": [{
            "id": "https://example.org/canvas/1",
            "type": "Canvas",
            "width": 3000, "height": 4000,
            "annotations": [{
              "id": "https://example.org/annotations/1",
              "type": "AnnotationPage",
              "label": {"en": ["Text of page 1"]}
            }]
          }]
        }
        """;

        var pages = new ManifestReducer().Reduce(manifest);

        pages.Count.ShouldBe(1);
        pages[0].TextUri.ShouldBe("https://example.org/annotations/1");
        pages[0].Format.ShouldBe(W3cAnnotationTextFormatProvider.FormatSentinel);
    }

    [Fact]
    public void ManifestReducer_SeeAlsoPreferredOverAnnotationPage()
    {
        var manifest = $$"""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "id": "https://example.org/manifest",
          "type": "Manifest",
          "items": [{
            "id": "https://example.org/canvas/1",
            "type": "Canvas",
            "width": 3000, "height": 4000,
            "seeAlso": [{"id": "https://example.org/alto/1.xml", "profile": "http://www.loc.gov/standards/alto/v3/alto.xsd", "format": "text/xml"}],
            "annotations": [{
              "id": "https://example.org/annotations/1",
              "type": "AnnotationPage"
            }]
          }]
        }
        """;

        var pages = new ManifestReducer().Reduce(manifest);

        pages[0].TextUri.ShouldBe("https://example.org/alto/1.xml");
        pages[0].Format.ShouldNotBe(W3cAnnotationTextFormatProvider.FormatSentinel);
    }

    [Fact]
    public void ManifestReducer_EmbeddedAnnotationPageItems_NotTreatedAsExternalPage()
    {
        // A canvas whose annotations have embedded items should not be double-counted
        // as an external annotation page.
        var manifest = $$"""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "id": "https://example.org/manifest",
          "type": "Manifest",
          "items": [{
            "id": "https://example.org/canvas/1",
            "type": "Canvas",
            "width": 3000, "height": 4000,
            "annotations": [{
              "id": "https://example.org/annotations/1",
              "type": "AnnotationPage",
              "items": [{
                "type": "Annotation",
                "motivation": "supplementing",
                "body": {"type": "TextualBody", "value": "some text"},
                "target": "https://example.org/canvas/1#xywh=0,0,100,20"
              }]
            }]
          }]
        }
        """;

        // The canvas has embedded items but no recognised format (not ALTO/hOCR/VTT),
        // and FindExternalAnnotationPage should skip it because items are present.
        // Result: Text = null (sparse), not a sentinel URL.
        var pages = new ManifestReducer().Reduce(manifest);

        pages.Count.ShouldBe(1);
        pages[0].TextUri.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (TextServices.Core.Models.Text Text, TextServices.Core.Models.AutoComplete AutoComplete)
        Build(string json, string canvasId)
    {
        var acc = new TextAccumulator();
        Provider.ProcessPage(acc, json, canvasId, 3000, 4000);
        var result = acc.Build();
        return (result.Text, result.AutoComplete);
    }

    private static string AnnotationPage(IEnumerable<string> items) =>
        $$"""{"type":"AnnotationPage","items":[{{string.Join(",", items)}}]}""";

    private static string Annotation(string value, string target, string motivation = "supplementing") =>
        $$"""{"type":"Annotation","motivation":"{{motivation}}","body":{"type":"TextualBody","value":{{System.Text.Json.JsonSerializer.Serialize(value)}}},"target":"{{target}}"}""";

    private static string AnnotationSpecificResource(string value, string source, string fragmentValue)
    {
        // Built via JsonObject to avoid triple-closing-brace ambiguity in raw string literals.
        var obj = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "Annotation",
            ["motivation"] = "supplementing",
            ["body"] = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "TextualBody",
                ["value"] = value,
            },
            ["target"] = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "SpecificResource",
                ["source"] = source,
                ["selector"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "FragmentSelector",
                    ["value"] = fragmentValue,
                },
            },
        };
        return obj.ToJsonString();
    }
}
