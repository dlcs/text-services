using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Shouldly;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;

namespace TextServices.Tests.BuilderApi;

/// <summary>
/// Unit tests for <see cref="ManifestSynthesiser"/> — verifies that the synthesised
/// IIIF Presentation 3 JSON has the correct structure for a variety of page inputs.
/// </summary>
public class ManifestSynthesiserTests
{
    private static ManifestSynthesiser Sut(string searchApiBaseUrl = "", bool allowFileProxy = false) =>
        new(Options.Create(new TextServicesOptions { SearchApiBaseUrl = searchApiBaseUrl, AllowFileImageProxy = allowFileProxy }));

    // -------------------------------------------------------------------------
    // Top-level manifest shape
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_ProducesValidJson()
    {
        var pages = new List<PageInstruction> { new() { Id = "https://example.org/c/1" } };

        var json = Sut().Synthesise(pages);

        JsonNode.Parse(json).ShouldNotBeNull(); // should not throw
    }

    [Fact]
    public void Synthesise_ManifestTypeIsManifest()
    {
        var pages = new List<PageInstruction> { new() { Id = "https://example.org/c/1" } };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;

        result["type"]!.GetValue<string>().ShouldBe("Manifest");
    }

    [Fact]
    public void Synthesise_ManifestIdIsEmptyString()
    {
        // The text-augmented endpoint patches the id at serve time,
        // so the synthesised manifest must start with an empty id.
        var pages = new List<PageInstruction> { new() { Id = "https://example.org/c/1" } };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;

        result["id"]!.GetValue<string>().ShouldBe(string.Empty);
    }

    [Fact]
    public void Synthesise_HasPresentation3Context()
    {
        var pages = new List<PageInstruction> { new() { Id = "https://example.org/c/1" } };

        var json = Sut().Synthesise(pages);

        // iiif-net emits the http:// form of the context URI.
        json.ShouldContain("iiif.io/api/presentation/3/context.json");
    }

    // -------------------------------------------------------------------------
    // Canvas count
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_SinglePage_ProducesOneCanvas()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;

        result["items"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void Synthesise_MultiplePages_AllCanvasesPresent()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1" },
            new() { Id = "https://example.org/c/2" },
            new() { Id = "https://example.org/c/3" },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;

        result["items"]!.AsArray().Count.ShouldBe(3);
    }

    // -------------------------------------------------------------------------
    // Canvas id
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_Canvas_IdMatchesPageInstruction()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/canvas/42" },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvas = result["items"]![0]!;

        canvas["id"]!.GetValue<string>().ShouldBe("https://example.org/canvas/42");
    }

    // -------------------------------------------------------------------------
    // Canvas width / height
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_Canvas_SetsWidthAndHeight()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1234, Height = 5678 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvas = result["items"]![0]!;

        canvas["width"]!.GetValue<int>().ShouldBe(1234);
        canvas["height"]!.GetValue<int>().ShouldBe(5678);
    }

    [Fact]
    public void Synthesise_Canvas_OmitsWidthAndHeightWhenZero()
    {
        // Audio/video canvases have no pixel dimensions.
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/audio", Width = 0, Height = 0 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvas = result["items"]![0]!;

        canvas["width"].ShouldBeNull();
        canvas["height"].ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Canvas duration
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_Canvas_SetsDurationWhenPresent()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/audio", Duration = 42.5 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvas = result["items"]![0]!;

        canvas["duration"]!.GetValue<double>().ShouldBe(42.5);
    }

    [Fact]
    public void Synthesise_Canvas_OmitsDurationWhenNull()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvas = result["items"]![0]!;

        canvas["duration"].ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Painting annotations (ImageUri)
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_Canvas_NoPaintingAnnotationWhenNoImageUri()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvas = result["items"]![0]!;

        // No items array when no image.
        canvas["items"].ShouldBeNull();
    }

    [Fact]
    public void Synthesise_Canvas_HasPaintingAnnotationWhenImageUriSet()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200,
                    ImageUri = "https://example.org/images/1.jpg" },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvas = result["items"]![0]!;

        var annoPages = canvas["items"]!.AsArray();
        annoPages.Count.ShouldBe(1);

        var annoPage = annoPages[0]!;
        annoPage["type"]!.GetValue<string>().ShouldBe("AnnotationPage");

        var annos = annoPage["items"]!.AsArray();
        annos.Count.ShouldBe(1);

        var anno = annos[0]!;
        anno["type"]!.GetValue<string>().ShouldBe("Annotation");
        anno["motivation"]!.GetValue<string>().ShouldBe("painting");
    }

    // -------------------------------------------------------------------------
    // Painting annotation body URL — imageUri resolution
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_PaintingAnnotationBody_HttpsUri_EchoedAsIs()
    {
        // http/https imageUri is already a resolved image URL — use as-is regardless
        // of whether it looks like an IIIF image service root or a direct image request.
        const string imageUri = "https://example.org/iiif/image/page1/full/656,1024/0/default.jpg";

        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 2679, Height = 4179,
                    ImageUri = imageUri },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var anno = result["items"]![0]!["items"]![0]!["items"]![0]!;

        anno["body"]!["id"]!.GetValue<string>().ShouldBe(imageUri);
    }

    [Fact]
    public void Synthesise_PaintingAnnotationBody_FileUri_ProxyEnabled_UsesProxyUrl()
    {
        // When AllowFileImageProxy is true and SearchApiBaseUrl is set, file:// body.id
        // is rewritten to a /proxy/image URL on the Search API.
        const string imageUri = "file:///C:/fixtures/b2888193x/images/b2888193x_0001.jpg";
        const string searchBaseUrl = "http://localhost:5294";
        var expected = $"{searchBaseUrl}/proxy/image?uri={Uri.EscapeDataString(imageUri)}";

        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 2679, Height = 4179,
                    ImageUri = imageUri },
        };

        var result = JsonNode.Parse(Sut(searchBaseUrl, allowFileProxy: true).Synthesise(pages))!;
        var anno = result["items"]![0]!["items"]![0]!["items"]![0]!;

        anno["body"]!["id"]!.GetValue<string>().ShouldBe(expected);
    }

    [Fact]
    public void Synthesise_PaintingAnnotationBody_FileUri_ProxyDisabled_OmitsPaintingAnnotation()
    {
        // When AllowFileImageProxy is false (the safe default), the painting annotation is
        // omitted entirely — no file path is embedded in the manifest.
        const string imageUri = "file:///C:/fixtures/b2888193x/images/b2888193x_0001.jpg";

        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 2679, Height = 4179,
                    ImageUri = imageUri },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;

        result["items"]![0]!["items"].ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Painting annotation target
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_PaintingAnnotationTarget_IsCanvasId()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200,
                    ImageUri = "https://example.org/images/1.jpg" },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var anno = result["items"]![0]!["items"]![0]!["items"]![0]!;

        // iiif-net serialises a canvas-only target as a plain string id.
        var targetNode = anno["target"]!;
        var targetId = targetNode is { } n && n.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? n.GetValue<string>()
            : targetNode["id"]!.GetValue<string>();
        targetId.ShouldBe("https://example.org/c/1");
    }

    [Fact]
    public void Synthesise_MixedPages_OnlyImagePagesHavePaintingAnnotations()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200,
                    ImageUri = "https://example.org/img/1.jpg" },
            new() { Id = "https://example.org/c/2", Width = 100, Height = 200 }, // no image
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvases = result["items"]!.AsArray();

        canvases[0]!["items"].ShouldNotBeNull();
        canvases[1]!["items"].ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // pdf-type pages
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_PdfTypePage_IsExcludedFromManifest()
    {
        // pdf-type pages embed an existing PDF — they produce no canvas.
        var pages = new List<PageInstruction>
        {
            new() { Type = "pdf", Input = "file:///path/to/cover.pdf" },
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;

        // Only the normal page becomes a canvas.
        result["items"]!.AsArray().Count.ShouldBe(1);
        result["items"]![0]!["id"]!.GetValue<string>().ShouldBe("https://example.org/c/1");
    }

    [Fact]
    public void Synthesise_AllPdfTypePages_ProducesNoCanvases()
    {
        var pages = new List<PageInstruction>
        {
            new() { Type = "pdf", Input = "file:///path/to/a.pdf" },
            new() { Type = "PDF", Input = "file:///path/to/b.pdf" }, // case-insensitive
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;

        // iiif-net omits the items key entirely when the canvas list is empty.
        var items = result["items"] as JsonArray;
        (items?.Count ?? 0).ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // Custom-type pages
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_CustomTypePage_HasCanvasButNoPaintingAnnotation()
    {
        // Custom-type pages (e.g. "redacted") get a canvas but no painting annotation
        // — the PDF renders a centred message instead.
        var pages = new List<PageInstruction>
        {
            new() { Type = "redacted", Id = "https://example.org/c/r1",
                    Width = 1000, Height = 1500 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvases = result["items"]!.AsArray();

        canvases.Count.ShouldBe(1);
        var canvas = canvases[0]!;
        canvas["id"]!.GetValue<string>().ShouldBe("https://example.org/c/r1");
        canvas["width"]!.GetValue<int>().ShouldBe(1000);
        canvas["height"]!.GetValue<int>().ShouldBe(1500);
        // No painting annotation.
        canvas["items"].ShouldBeNull();
    }

    [Fact]
    public void Synthesise_MixedSequence_PdfExcluded_CustomAndNormalIncluded()
    {
        var pages = new List<PageInstruction>
        {
            new() { Type = "pdf",      Input  = "file:///cover.pdf" },
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200,
                    ImageUri = "https://example.org/img/1.jpg" },
            new() { Type = "redacted", Id     = "https://example.org/c/r1",
                    Width = 100, Height = 200 },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var canvases = result["items"]!.AsArray();

        // pdf-type excluded; normal + custom = 2 canvases.
        canvases.Count.ShouldBe(2);
        canvases[0]!["id"]!.GetValue<string>().ShouldBe("https://example.org/c/1");
        canvases[0]!["items"].ShouldNotBeNull(); // has painting annotation
        canvases[1]!["id"]!.GetValue<string>().ShouldBe("https://example.org/c/r1");
        canvases[1]!["items"].ShouldBeNull();    // custom type: no painting annotation
    }

    // -------------------------------------------------------------------------
    // Input alias for normal pages
    // -------------------------------------------------------------------------

    [Fact]
    public void Synthesise_NormalPage_InputUsedAsImageUriAlias()
    {
        // When ImageUri is absent, Input is treated as the image URI for normal pages.
        const string imageUri = "https://example.org/images/page1.jpg";

        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200,
                    Input = imageUri },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var anno = result["items"]![0]!["items"]![0]!["items"]![0]!;

        anno["body"]!["id"]!.GetValue<string>().ShouldBe(imageUri);
    }

    [Fact]
    public void Synthesise_NormalPage_ImageUriTakesPrecedenceOverInput()
    {
        // ImageUri wins when both are set.
        const string imageUri = "https://example.org/images/correct.jpg";
        const string input = "https://example.org/images/wrong.jpg";

        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 100, Height = 200,
                    ImageUri = imageUri, Input = input },
        };

        var result = JsonNode.Parse(Sut().Synthesise(pages))!;
        var anno = result["items"]![0]!["items"]![0]!["items"]![0]!;

        anno["body"]!["id"]!.GetValue<string>().ShouldBe(imageUri);
    }
}
