using Shouldly;
using TextServices.Builder.Api.Services;

namespace TextServices.Tests.BuilderApi;

public class ManifestReducerTests
{
    private readonly ManifestReducer _reducer = new();

    // -------------------------------------------------------------------------
    // v3 detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduce_V3Context_Accepted()
    {
        var json = Manifest(canvases: [Canvas("https://example.org/c/1", 100, 200)]);
        var pages = _reducer.Reduce(json);
        pages.Count.ShouldBe(1);
    }

    [Fact]
    public void Reduce_V2Context_Throws()
    {
        var json = """
            {
              "@context": "http://iiif.io/api/presentation/2/context.json",
              "type": "Manifest",
              "items": []
            }
            """;
        Should.Throw<InvalidOperationException>(() => _reducer.Reduce(json))
            .Message.ShouldContain("v3");
    }

    [Fact]
    public void Reduce_NoContext_WithTypeAndItems_Accepted()
    {
        var json = """
            {
              "type": "Manifest",
              "id": "https://example.org/m",
              "items": []
            }
            """;
        var pages = _reducer.Reduce(json);
        pages.ShouldBeEmpty();
    }

    [Fact]
    public void Reduce_ContextAsArray_V3EntryPresent_Accepted()
    {
        var json = """
            {
              "@context": [
                "http://iiif.io/api/presentation/3/context.json",
                "http://www.w3.org/ns/anno.jsonld"
              ],
              "type": "Manifest",
              "items": []
            }
            """;
        var pages = _reducer.Reduce(json);
        pages.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Canvas extraction
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduce_ExtractsIdWidthHeight()
    {
        var json = Manifest(canvases: [Canvas("https://example.org/c/1", 4000, 6000)]);
        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(1);
        pages[0].Id.ShouldBe("https://example.org/c/1");
        pages[0].Width.ShouldBe(4000);
        pages[0].Height.ShouldBe(6000);
    }

    [Fact]
    public void Reduce_MultipleCanvases_AllReturned()
    {
        var json = Manifest(canvases: [
            Canvas("https://example.org/c/1", 100, 200),
            Canvas("https://example.org/c/2", 300, 400),
            Canvas("https://example.org/c/3", 500, 600),
        ]);
        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(3);
        pages[1].Id.ShouldBe("https://example.org/c/2");
    }

    [Fact]
    public void Reduce_NoItems_ReturnsEmpty()
    {
        var json = Manifest(canvases: []);
        var pages = _reducer.Reduce(json);
        pages.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // ALTO seeAlso detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduce_AltoByProfileUri_DetectsLink()
    {
        var json = Manifest(canvases: [CanvasWithAlto(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/alto/1.xml",
            profile: "http://www.loc.gov/standards/alto/ns-v3#",
            label: null)]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/alto/1.xml");
    }

    [Fact]
    public void Reduce_AltoByProfileContainingAlto_CaseInsensitive()
    {
        var json = Manifest(canvases: [CanvasWithAlto(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/alto/1.xml",
            profile: "https://schemas.example.org/ALTO/ns-v2",
            label: null)]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/alto/1.xml");
    }

    [Fact]
    public void Reduce_AltoByLangMapLabel_Detected()
    {
        var json = Manifest(canvases: [CanvasWithAltoLangMapLabel(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/alto/1.xml",
            labelKey: "none",
            labelValue: "METS-ALTO")]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/alto/1.xml");
    }

    [Fact]
    public void Reduce_AltoByLangMapLabel_AltoLowercase_Detected()
    {
        var json = Manifest(canvases: [CanvasWithAltoLangMapLabel(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/alto/1.xml",
            labelKey: "en",
            labelValue: "alto xml")]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/alto/1.xml");
    }

    [Fact]
    public void Reduce_NonAltoSeeAlso_TextIsNull()
    {
        var json = Manifest(canvases: [CanvasWithAlto(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/tei/1.xml",
            profile: "http://www.tei-c.org/ns/1.0",
            label: "TEI")]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBeNull();
    }

    [Fact]
    public void Reduce_NoSeeAlso_TextIsNull()
    {
        var json = Manifest(canvases: [Canvas("https://example.org/c/1", 100, 200)]);
        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBeNull();
    }

    [Fact]
    public void Reduce_MultipleSeeAlso_FirstAltoWins()
    {
        // Canvas has a TEI link followed by an ALTO link.
        var json = Manifest(canvases: [
            $$"""
            {
              "id": "https://example.org/c/1",
              "type": "Canvas",
              "width": 100,
              "height": 200,
              "seeAlso": [
                { "id": "https://example.org/tei/1.xml",  "profile": "http://www.tei-c.org/ns/1.0" },
                { "id": "https://example.org/alto/1.xml", "profile": "http://www.loc.gov/standards/alto/ns-v3#" }
              ]
            }
            """
        ]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/alto/1.xml");
    }

    [Fact]
    public void Reduce_SeeAlsoAsSingleObject_Detected()
    {
        // seeAlso may be a single object rather than an array in older v3 documents.
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/1",
              "type": "Canvas",
              "width": 100,
              "height": 200,
              "seeAlso": {
                "id": "https://example.org/alto/1.xml",
                "profile": "http://www.loc.gov/standards/alto/ns-v3#"
              }
            }
            """
        ]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/alto/1.xml");
    }

    // -------------------------------------------------------------------------
    // hOCR seeAlso detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduce_HocrByMimeTypeProfile_DetectsLink()
    {
        var json = Manifest(canvases: [CanvasWithAlto(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/ocr/1.html",
            profile: "text/vnd.hocr+html",
            label: null)]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/ocr/1.html");
        pages[0].Profile.ShouldBe("text/vnd.hocr+html");
    }

    [Fact]
    public void Reduce_HocrByLabel_DetectsLink()
    {
        var json = Manifest(canvases: [CanvasWithAltoLangMapLabel(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/ocr/1.html",
            labelKey: "en",
            labelValue: "Tesseract hOCR output")]);

        var pages = _reducer.Reduce(json);
        pages[0].TextUri.ShouldBe("https://example.org/ocr/1.html");
    }

    [Fact]
    public void Reduce_HocrProfile_StoredOnPageInstruction()
    {
        var json = Manifest(canvases: [CanvasWithAlto(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/ocr/1.html",
            profile: "text/vnd.hocr+html",
            label: null)]);

        var pages = _reducer.Reduce(json);
        pages[0].Profile.ShouldBe("text/vnd.hocr+html");
    }

    [Fact]
    public void Reduce_AltoProfile_StoredOnPageInstruction()
    {
        var json = Manifest(canvases: [CanvasWithAlto(
            "https://example.org/c/1", 100, 200,
            altoUri: "https://example.org/alto/1.xml",
            profile: "http://www.loc.gov/standards/alto/v3/alto.xsd",
            label: null)]);

        var pages = _reducer.Reduce(json);
        pages[0].Profile.ShouldBe("http://www.loc.gov/standards/alto/v3/alto.xsd");
    }

    // -------------------------------------------------------------------------
    // Sparse manifests
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduce_SparseManifest_CanvasesWithoutAltoIncluded()
    {
        var json = Manifest(canvases: [
            Canvas("https://example.org/c/1", 100, 200),                               // no ALTO
            CanvasWithAlto("https://example.org/c/2", 100, 200,
                "https://example.org/alto/2.xml", "alto", null),
            Canvas("https://example.org/c/3", 100, 200),                               // no ALTO
        ]);

        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(3);
        pages[0].TextUri.ShouldBeNull();
        pages[1].TextUri.ShouldBe("https://example.org/alto/2.xml");
        pages[2].TextUri.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Canvas dimension handling
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduce_DurationOnlyCanvas_SkippedSilently()
    {
        // Audio canvas: has duration but no width/height.
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 180.0
            }
            """
        ]);

        var pages = _reducer.Reduce(json);
        pages.ShouldBeEmpty();
    }

    [Fact]
    public void Reduce_CanvasWithWidthHeightAndDuration_Included()
    {
        // Video canvas with spatial dimensions — valid for ALTO text (unusual but not an error).
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/video",
              "type": "Canvas",
              "width": 1920,
              "height": 1080,
              "duration": 60.0
            }
            """
        ]);

        var pages = _reducer.Reduce(json);
        pages.Count.ShouldBe(1);
        pages[0].Id.ShouldBe("https://example.org/c/video");
        pages[0].Width.ShouldBe(1920);
        pages[0].Height.ShouldBe(1080);
    }

    [Fact]
    public void Reduce_MixedSpatialAndTemporalCanvases_OnlySpatialReturned()
    {
        var json = Manifest(canvases: [
            Canvas("https://example.org/c/1", 4000, 6000),
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 90.0
            }
            """,
            Canvas("https://example.org/c/2", 4000, 6000),
        ]);

        var pages = _reducer.Reduce(json);
        pages.Count.ShouldBe(2);
        pages[0].Id.ShouldBe("https://example.org/c/1");
        pages[1].Id.ShouldBe("https://example.org/c/2");
    }

    // -------------------------------------------------------------------------
    // VTT temporal canvas detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduce_VttViaSeeAlso_Format_IncludesCanvas()
    {
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 180.0,
              "seeAlso": [{
                "id": "https://example.org/transcript.vtt",
                "type": "Dataset",
                "format": "text/vtt"
              }]
            }
            """
        ]);

        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(1);
        pages[0].Id.ShouldBe("https://example.org/c/audio");
        pages[0].TextUri.ShouldBe("https://example.org/transcript.vtt");
        pages[0].Format.ShouldBe("text/vtt");
    }

    [Fact]
    public void Reduce_VttViaSeeAlso_Label_IncludesCanvas()
    {
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 180.0,
              "seeAlso": [{
                "id": "https://example.org/transcript.vtt",
                "type": "Dataset",
                "label": { "none": ["WebVTT"] }
              }]
            }
            """
        ]);

        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(1);
        pages[0].Id.ShouldBe("https://example.org/c/audio");
        pages[0].TextUri.ShouldBe("https://example.org/transcript.vtt");
    }

    [Fact]
    public void Reduce_VttViaAnnotations_Supplementing_IncludesCanvas()
    {
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 180.0,
              "annotations": [{
                "type": "AnnotationPage",
                "items": [{
                  "type": "Annotation",
                  "motivation": "supplementing",
                  "body": {
                    "id": "https://example.org/transcript.vtt",
                    "type": "Text",
                    "format": "text/vtt"
                  },
                  "target": "https://example.org/c/audio"
                }]
              }]
            }
            """
        ]);

        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(1);
        pages[0].Id.ShouldBe("https://example.org/c/audio");
        pages[0].TextUri.ShouldBe("https://example.org/transcript.vtt");
        pages[0].Format.ShouldBe("text/vtt");
    }

    [Fact]
    public void Reduce_VttViaAnnotations_MotivationAsArray_Handled()
    {
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 180.0,
              "annotations": [{
                "type": "AnnotationPage",
                "items": [{
                  "type": "Annotation",
                  "motivation": ["supplementing"],
                  "body": {
                    "id": "https://example.org/transcript.vtt",
                    "type": "Text",
                    "format": "text/vtt"
                  },
                  "target": "https://example.org/c/audio"
                }]
              }]
            }
            """
        ]);

        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(1);
        pages[0].TextUri.ShouldBe("https://example.org/transcript.vtt");
    }

    [Fact]
    public void Reduce_VttCanvas_HasZeroDimensions()
    {
        var json = Manifest(canvases: [
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 180.0,
              "seeAlso": [{
                "id": "https://example.org/transcript.vtt",
                "type": "Dataset",
                "format": "text/vtt"
              }]
            }
            """
        ]);

        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(1);
        pages[0].Width.ShouldBe(0);
        pages[0].Height.ShouldBe(0);
    }

    [Fact]
    public void Reduce_MixedSpatialAndTemporalCanvases_BothIncludedWhenVttPresent()
    {
        var json = Manifest(canvases: [
            Canvas("https://example.org/c/1", 4000, 6000),
            """
            {
              "id": "https://example.org/c/audio",
              "type": "Canvas",
              "duration": 90.0,
              "seeAlso": [{
                "id": "https://example.org/transcript.vtt",
                "type": "Dataset",
                "format": "text/vtt"
              }]
            }
            """,
        ]);

        var pages = _reducer.Reduce(json);

        pages.Count.ShouldBe(2);
        pages[0].Id.ShouldBe("https://example.org/c/1");
        pages[1].Id.ShouldBe("https://example.org/c/audio");
        pages[1].Format.ShouldBe("text/vtt");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string Manifest(IEnumerable<string> canvases) =>
        $$"""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "id": "https://example.org/manifest",
          "type": "Manifest",
          "items": [{{string.Join(",", canvases)}}]
        }
        """;

    private static string Canvas(string id, int width, int height) =>
        $$"""
        {
          "id": "{{id}}",
          "type": "Canvas",
          "width": {{width}},
          "height": {{height}}
        }
        """;

    private static string CanvasWithAlto(string id, int width, int height,
        string altoUri, string? profile, string? label)
    {
        // Regular interpolated strings (not raw) so that \" correctly becomes ".
        var profileJson = profile != null ? $"\"profile\": \"{profile}\"," : "";
        var labelJson   = label   != null ? $"\"label\": \"{label}\","     : "";
        return $$"""
        {
          "id": "{{id}}",
          "type": "Canvas",
          "width": {{width}},
          "height": {{height}},
          "seeAlso": [{
            "id": "{{altoUri}}",
            {{profileJson}}
            {{labelJson}}
            "type": "Dataset"
          }]
        }
        """;
    }

    private static string CanvasWithAltoLangMapLabel(string id, int width, int height,
        string altoUri, string labelKey, string labelValue) =>
        $$"""
        {
          "id": "{{id}}",
          "type": "Canvas",
          "width": {{width}},
          "height": {{height}},
          "seeAlso": [{
            "id": "{{altoUri}}",
            "type": "Dataset",
            "label": { "{{labelKey}}": ["{{labelValue}}"] }
          }]
        }
        """;
}
