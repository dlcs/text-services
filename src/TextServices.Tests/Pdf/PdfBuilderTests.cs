using System.Net;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Pdf;

namespace TextServices.Tests.Pdf;

/// <summary>
/// Unit tests for <see cref="PdfBuilder"/> page sizing and assembly logic.
/// Tests use a 1×1 pixel PNG (0.48 pt at 150 DPI) as a stand-in for real canvas images so
/// that the dimension values are deterministic without needing a real image service.
/// </summary>
public sealed class PdfBuilderTests
{
    // 1×1 white pixel PNG — iText7 parses this as a 1×1 image.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    // At 150 DPI, 1 pixel → 1 * 72 / 150 = 0.48 pt
    private const float TinyPt = 1f * 72f / 150f;
    private const float Delta = 0.02f;

    // -------------------------------------------------------------------------
    // ChooseImageUrl — tested via HTTP call tracking
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_NoService_FetchesStaticImageUrl()
    {
        var requested = new List<string?>();
        var factory = CreateFactory(req =>
        {
            requested.Add(req.RequestUri?.ToString());
            return OkImage(TinyPng);
        });
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            canvasId: "https://example.org/canvas/1",
            imageId: "https://example.org/image/1.jpg",
            canvasW: 2000, canvasH: 3000);

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, null, output, CancellationToken.None);

        requested.ShouldContain("https://example.org/image/1.jpg");
    }

    [Fact]
    public async Task BuildAsync_WithServiceAndSizes_FetchesBestFitSizeUrl()
    {
        var requested = new List<string?>();
        var factory = CreateFactory(req =>
        {
            requested.Add(req.RequestUri?.ToString());
            return OkImage(TinyPng);
        });
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            canvasId: "https://example.org/canvas/1",
            imageId: "https://example.org/image/full/full/0/default.jpg",
            canvasW: 4000, canvasH: 6000,
            serviceId: "https://example.org/image",
            sizes: [(500, 750), (1000, 1500), (2000, 3000)]);

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, null, output, CancellationToken.None);

        // Largest size that fits within MaxEdgePx=2000
        requested.ShouldContain("https://example.org/image/full/1000,1500/0/default.jpg");
    }

    [Fact]
    public async Task BuildAsync_WithServiceNoSizes_FetchesBangUrl()
    {
        var requested = new List<string?>();
        var factory = CreateFactory(req =>
        {
            requested.Add(req.RequestUri?.ToString());
            return OkImage(TinyPng);
        });
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            canvasId: "https://example.org/canvas/1",
            imageId: "https://example.org/image/full/full/0/default.jpg",
            canvasW: 4000, canvasH: 6000,
            serviceId: "https://example.org/image",
            sizes: []);

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, null, output, CancellationToken.None);

        requested.ShouldContain("https://example.org/image/full/!2000,2000/0/default.jpg");
    }

    // -------------------------------------------------------------------------
    // Page sizing — sourceUri path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_ImagePage_DimensionsFromFetchedImage_NotCanvas()
    {
        // Canvas is large (2000×3000) but fetched image is 1×1 (TinyPng).
        // Page should be sized from the fetched image, not the canvas.
        var factory = CreateFactory(_ => OkImage(TinyPng));
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            "https://example.org/canvas/1", "https://example.org/image/1.jpg",
            canvasW: 2000, canvasH: 3000);

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, null, output, CancellationToken.None);

        var pageSize = ReadFirstPageSize(output);
        pageSize.GetWidth().ShouldBe(TinyPt, Delta);
        pageSize.GetHeight().ShouldBe(TinyPt, Delta);
    }

    [Fact]
    public async Task BuildAsync_ImageFetchFails_AddsFallbackPageFromCanvasDimensions()
    {
        // Canvas is 100×150; fetching the image throws. Fallback page uses canvas dims at 150 DPI.
        // 100 * 72 / 150 = 48 pt; 150 * 72 / 150 = 72 pt.
        var factory = CreateFactory(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            "https://example.org/canvas/1", "https://example.org/image/1.jpg",
            canvasW: 100, canvasH: 150);

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, null, output, CancellationToken.None);

        var pdf = ReadPdf(output);
        pdf.GetNumberOfPages().ShouldBe(1);

        var pageSize = pdf.GetFirstPage().GetPageSize();
        pageSize.GetWidth().ShouldBe(48.0f, Delta);
        pageSize.GetHeight().ShouldBe(72.0f, Delta);
    }

    // -------------------------------------------------------------------------
    // Page sizing — sourceData path with custom types
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_CustomTypePage_MatchesAdjacentImagePageSize()
    {
        // Sequence: image canvas → custom "redacted" page.
        // The redacted page must be the same size as the image page (TinyPt × TinyPt).
        var factory = CreateFactory(_ => OkImage(TinyPng));
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            "https://example.org/canvas/1", "https://example.org/image/1.jpg",
            canvasW: 2000, canvasH: 3000);
        var pageSeq = """
            {
              "pages": [
                { "canvasId": "https://example.org/canvas/1" },
                { "type": "redacted", "message": "Redacted" }
              ]
            }
            """;

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, pageSeq, output, CancellationToken.None);

        var pdf = ReadPdf(output);
        pdf.GetNumberOfPages().ShouldBe(2);

        var imagePage = pdf.GetPage(1).GetPageSize();
        var customPage = pdf.GetPage(2).GetPageSize();
        customPage.GetWidth().ShouldBe(imagePage.GetWidth(), Delta);
        customPage.GetHeight().ShouldBe(imagePage.GetHeight(), Delta);
    }

    [Fact]
    public async Task BuildAsync_CustomTypeBeforeImagePage_FallsBackToA4()
    {
        // Sequence: custom page first (before any image), then image canvas.
        // When no pdf-type pages are present, DetermineReferenceSizeAsync is NOT called,
        // so refWidthPt/refHeightPt starts at A4 defaults.
        var factory = CreateFactory(_ => OkImage(TinyPng));
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            "https://example.org/canvas/1", "https://example.org/image/1.jpg",
            canvasW: 2000, canvasH: 3000);
        var pageSeq = """
            {
              "pages": [
                { "type": "redacted", "message": "Redacted" },
                { "canvasId": "https://example.org/canvas/1" }
              ]
            }
            """;

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, pageSeq, output, CancellationToken.None);

        var pdf = ReadPdf(output);
        pdf.GetNumberOfPages().ShouldBe(2);

        // First page is custom before any image: ref not set yet, uses A4 fallback.
        var customPage = pdf.GetPage(1).GetPageSize();
        customPage.GetWidth().ShouldBe(PageSize.A4.GetWidth(), Delta);
        customPage.GetHeight().ShouldBe(PageSize.A4.GetHeight(), Delta);
    }

    [Fact]
    public async Task BuildAsync_PdfTypePagePrecedesImagePage_PreScansForReferenceSize()
    {
        // Sequence: pdf-type first, then image canvas.
        // DetermineReferenceSizeAsync pre-scans and finds the image canvas,
        // so the embedded PDF is sized to match the (fetched) image dimensions.
        var requestCount = 0;
        var factory = CreateFactory(req =>
        {
            requestCount++;
            return req.RequestUri?.ToString().EndsWith(".pdf") == true
                ? OkPdf(CreateMinimalPdf())
                : OkImage(TinyPng);
        });
        var sut = MakeSut(factory);
        var text = MakeText(["https://example.org/canvas/1"]);
        var manifest = OneCanvasManifest(
            "https://example.org/canvas/1", "https://example.org/image/1.jpg",
            canvasW: 2000, canvasH: 3000);
        var pageSeq = """
            {
              "pages": [
                { "type": "pdf", "input": "https://example.org/cover.pdf" },
                { "canvasId": "https://example.org/canvas/1" }
              ]
            }
            """;

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, pageSeq, output, CancellationToken.None);

        var pdf = ReadPdf(output);
        pdf.GetNumberOfPages().ShouldBe(2);

        var embeddedPage = pdf.GetPage(1).GetPageSize();
        var imagePage = pdf.GetPage(2).GetPageSize();
        // The embedded PDF was scaled to the image page reference size.
        embeddedPage.GetWidth().ShouldBe(imagePage.GetWidth(), Delta);
        embeddedPage.GetHeight().ShouldBe(imagePage.GetHeight(), Delta);
    }

    [Fact]
    public async Task BuildAsync_MultipleCanvases_EmitsCorrectPageCount()
    {
        var factory = CreateFactory(_ => OkImage(TinyPng));
        var sut = MakeSut(factory);
        var canvasIds = new[]
        {
            "https://example.org/canvas/1",
            "https://example.org/canvas/2",
            "https://example.org/canvas/3",
        };
        var text = MakeText(canvasIds);
        var manifest = MultiCanvasManifest(canvasIds, "https://example.org/image/{n}.jpg");

        using var output = new MemoryStream();
        await sut.BuildAsync(text, manifest, null, output, CancellationToken.None);

        ReadPdf(output).GetNumberOfPages().ShouldBe(3);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PdfBuilder MakeSut(IHttpClientFactory factory)
        => new(factory, NullLogger<PdfBuilder>.Instance);

    private static Text MakeText(string[] canvasIds)
    {
        var images = canvasIds
            .Select((id, idx) => new Image { ImageIdentifier = id, StartCharacter = idx })
            .ToArray();
        return new Text { Images = images };
    }

    private static string OneCanvasManifest(
        string canvasId, string imageId,
        int canvasW, int canvasH,
        string? serviceId = null,
        (int W, int H)[]? sizes = null)
    {
        var sizeJson = sizes is { Length: > 0 }
            ? ",\"sizes\":[" + string.Join(",", sizes.Select(s => $"{{\"width\":{s.W},\"height\":{s.H}}}")) + "]"
            : string.Empty;
        var serviceJson = serviceId != null
            ? $",\"service\":{{\"id\":\"{serviceId}\"{sizeJson}}}"
            : string.Empty;

        return $"{{\"items\":[{{\"id\":\"{canvasId}\",\"width\":{canvasW},\"height\":{canvasH}," +
               $"\"items\":[{{\"items\":[{{\"motivation\":\"painting\",\"body\":{{\"type\":\"Image\"," +
               $"\"id\":\"{imageId}\",\"width\":{canvasW},\"height\":{canvasH}{serviceJson}}}}}]}}]}}]}}";
    }

    private static string MultiCanvasManifest(string[] canvasIds, string imageTemplate)
    {
        var items = canvasIds.Select((id, i) =>
            $"{{\"id\":\"{id}\",\"width\":200,\"height\":300," +
            $"\"items\":[{{\"items\":[{{\"motivation\":\"painting\",\"body\":{{\"type\":\"Image\"," +
            $"\"id\":\"{imageTemplate.Replace("{n}", (i + 1).ToString())}\",\"width\":200,\"height\":300}}}}]}}]}}"
        );
        return $"{{\"items\":[{string.Join(",", items)}]}}";
    }

    private static byte[] CreateMinimalPdf()
    {
        using var ms = new MemoryStream();
        using (var doc = new PdfDocument(new PdfWriter(ms)))
            doc.AddNewPage(PageSize.A4);
        return ms.ToArray();
    }

    private static IHttpClientFactory CreateFactory(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var client = new HttpClient(new FakeHandler(handler));
        return new SingleClientFactory(client);
    }

    private static IHttpClientFactory CreateFactory(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        => CreateFactory(req => Task.FromResult(handler(req)));

    private static Task<HttpResponseMessage> OkImage(byte[] bytes)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new("image/png") }
            }
        });

    private static Task<HttpResponseMessage> OkPdf(byte[] bytes)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new("application/pdf") }
            }
        });

    private static Rectangle ReadFirstPageSize(MemoryStream output)
    {
        output.Position = 0;
        using var reader = new PdfReader(output);
        using var pdf = new PdfDocument(reader);
        return pdf.GetFirstPage().GetPageSize();
    }

    private static PdfDocument ReadPdf(MemoryStream output)
    {
        output.Position = 0;
        return new PdfDocument(new PdfReader(output));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> fn)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) => fn(request);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
