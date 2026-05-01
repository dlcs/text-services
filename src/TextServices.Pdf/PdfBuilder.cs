using System.Text.Json.Nodes;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Microsoft.Extensions.Logging;
using TextServices.Core.Models;

namespace TextServices.Pdf;

/// <summary>
/// Builds a searchable PDF for a processed job: one page per canvas, with the canvas image
/// as the background and an invisible text layer (PDF Tr=3) derived from stored word positions.
/// </summary>
/// <remarks>
/// Image URLs are resolved from the stored manifest JSON, respecting a 2000 px longest-edge
/// limit, IIIF Image Service <c>sizes</c> arrays, and the static body URL as a fallback.
/// When <c>pageSequenceJson</c> is supplied the full page sequence is used (including
/// embedded-PDF and custom-type pages that have no canvas in the synthesised manifest).
/// </remarks>
public class PdfBuilder(IHttpClientFactory httpClientFactory, ILogger<PdfBuilder> logger)
{
    /// <summary>Named <see cref="HttpClient"/> used for fetching canvas images.</summary>
    public const string HttpClientName = "pdf";

    private const double TargetDpi  = 150.0;
    private const int    MaxEdgePx  = 2000;
    private const float  TextPageFontSize = 14f;

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a PDF and writes it to <paramref name="output"/>.
    /// </summary>
    /// <param name="text">Stored word/image index.</param>
    /// <param name="manifestJson">Stored manifest JSON (used to resolve image URLs).</param>
    /// <param name="pageSequenceJson">
    /// When non-null (sourceData jobs), the stored page-sequence JSON drives the assembly
    /// order, including any embedded-PDF and custom-type pages.
    /// When null (sourceUri jobs), pages are emitted in <paramref name="text"/>.Images order.
    /// </param>
    /// <param name="output">Stream to write the PDF to.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task BuildAsync(
        Text   text,
        string manifestJson,
        string? pageSequenceJson,
        Stream output,
        CancellationToken ct)
    {
        var manifestPages = ParseManifestPages(manifestJson);

        var pdfWriter = new PdfWriter(
            new NonClosingStream(output),
            new WriterProperties().SetFullCompressionMode(true));
        using var pdfDoc = new PdfDocument(pdfWriter);
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

        using var http = httpClientFactory.CreateClient(HttpClientName);

        if (pageSequenceJson != null)
        {
            // sourceData job: full assembly including pdf-embed and custom-type pages.
            var entries      = ParsePageSequence(pageSequenceJson);
            var textIndexMap = BuildCanvasIndexMap(text);

            // Pre-determine reference page size from the first image canvas so that
            // embedded PDFs (which can appear before any image page) can be scaled to match.
            var (refWidthPt, refHeightPt) = entries.Any(e =>
                    string.Equals(e.Type, "pdf", StringComparison.OrdinalIgnoreCase))
                ? await DetermineReferenceSizeAsync(entries, manifestPages, http, ct)
                : (PageSize.A4.GetWidth(), PageSize.A4.GetHeight());

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (string.Equals(entry.Type, "pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await EmbedPdfAsync(pdfDoc, http, entry.Input, refWidthPt, refHeightPt, ct);
                }
                else if (entry.Type != null)
                {
                    // Custom type (e.g. "redacted"): use the reference image-page size.
                    AddTextPage(pdfDoc, font, entry.Message ?? entry.Type,
                        refWidthPt, refHeightPt);
                }
                else
                {
                    // Normal canvas page — also update ref so later text pages stay in sync.
                    if (entry.CanvasId == null ||
                        !manifestPages.TryGetValue(entry.CanvasId, out var pageInfo))
                    {
                        logger.LogDebug(
                            "No manifest entry for canvas {CanvasId} — skipping", entry.CanvasId);
                        continue;
                    }
                    var textIndex = textIndexMap.TryGetValue(entry.CanvasId, out var ti) ? ti : -1;
                    (refWidthPt, refHeightPt) =
                        await AddPageAsync(pdfDoc, font, http, text, textIndex, pageInfo, ct);
                }
            }
        }
        else
        {
            // sourceUri job: emit one page per text.Images entry (original behaviour).
            for (var pageIndex = 0; pageIndex < text.Images.Length; pageIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var image = text.Images[pageIndex];

                if (!manifestPages.TryGetValue(image.ImageIdentifier, out var pageInfo))
                {
                    logger.LogDebug(
                        "No manifest entry for canvas {CanvasId} — skipping page",
                        image.ImageIdentifier);
                    continue;
                }

                await AddPageAsync(pdfDoc, font, http, text, pageIndex, pageInfo, ct);
            }
        }
        // pdfDoc disposed by using — flushes all content streams and closes PdfWriter.
    }

    // -------------------------------------------------------------------------
    // Page construction
    // -------------------------------------------------------------------------

    private async Task<(float widthPt, float heightPt)> AddPageAsync(
        PdfDocument   pdfDoc,
        PdfFont       font,
        HttpClient    http,
        Text          text,
        int           textIndex,   // index into text.Images; -1 = canvas has no text
        PageInfo      pageInfo,
        CancellationToken ct)
    {
        var imageUrl = ChooseImageUrl(pageInfo);

        ImageData imageData;
        try
        {
            var bytes = await http.GetByteArrayAsync(imageUrl, ct);
            imageData = ImageDataFactory.Create(bytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch image for canvas {CanvasId} from {Url} — emitting blank page",
                pageInfo.CanvasId, imageUrl);
            var fallbackW = (float)(pageInfo.CanvasWidth  * 72.0 / TargetDpi);
            var fallbackH = (float)(pageInfo.CanvasHeight * 72.0 / TargetDpi);
            pdfDoc.AddNewPage(new PageSize(fallbackW, fallbackH));
            return (fallbackW, fallbackH);
        }

        var imgWidthPx  = (int)imageData.GetWidth();
        var imgHeightPx = (int)imageData.GetHeight();

        var pageWidthPt  = (float)(imgWidthPx  * 72.0 / TargetDpi);
        var pageHeightPt = (float)(imgHeightPx * 72.0 / TargetDpi);

        var page   = pdfDoc.AddNewPage(new PageSize(pageWidthPt, pageHeightPt));
        var canvas = new PdfCanvas(page);

        // Background image — scale to fill the page using the CTM directly.
        canvas.AddImageWithTransformationMatrix(imageData, pageWidthPt, 0, 0, pageHeightPt, 0, 0);

        // Invisible text layer (omitted when this canvas has no text index).
        var words = textIndex >= 0
            ? text.Words.Values.Where(w => w.Idx == textIndex).ToList()
            : [];

        if (words.Count > 0 && pageInfo.CanvasWidth > 0 && pageInfo.CanvasHeight > 0)
        {
            var scaleX = pageWidthPt  / (float)pageInfo.CanvasWidth;
            var scaleY = pageHeightPt / (float)pageInfo.CanvasHeight;

            canvas.BeginText();
            canvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE);

            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(word.ContentRaw)) continue;

                var fontSize    = Math.Max(1f, word.H * scaleY);
                var x           = word.X * scaleX;
                // PDF origin is bottom-left; flip Y axis.
                var y           = pageHeightPt - (word.Y + word.H) * scaleY;
                var targetWidth = Math.Max(1f, word.W * scaleX);

                canvas.SetFontAndSize(font, fontSize);

                var naturalWidth = font.GetWidth(word.ContentRaw, fontSize);
                var hScale = naturalWidth > 0f ? targetWidth / naturalWidth * 100f : 100f;

                canvas.SetHorizontalScaling(hScale);
                canvas.SetTextMatrix(1, 0, 0, 1, x, y);
                canvas.ShowText(word.ContentRaw);
            }

            canvas.EndText();
        }

        canvas.Release();
        return (pageWidthPt, pageHeightPt);
    }

    /// <summary>
    /// Adds a page containing only centred text (used for custom page types such as
    /// "redacted" or "missing").  <paramref name="widthPt"/> and <paramref name="heightPt"/>
    /// should come from the last real image page so the text page matches its neighbours.
    /// </summary>
    private static void AddTextPage(
        PdfDocument pdfDoc,
        PdfFont     font,
        string      message,
        float       widthPt,
        float       heightPt)
    {
        var page   = pdfDoc.AddNewPage(new PageSize(widthPt, heightPt));
        var canvas = new PdfCanvas(page);

        var textWidth = font.GetWidth(message, TextPageFontSize);
        var x = (widthPt  - textWidth)        / 2f;
        var y = (heightPt - TextPageFontSize)  / 2f;

        canvas.BeginText();
        canvas.SetFontAndSize(font, TextPageFontSize);
        canvas.SetTextMatrix(1, 0, 0, 1, x, y);
        canvas.ShowText(message);
        canvas.EndText();
        canvas.Release();
    }

    /// <summary>
    /// Fetches the PDF at <paramref name="inputUri"/>, scales each of its pages to fit
    /// within <paramref name="targetWidthPt"/> × <paramref name="targetHeightPt"/> (preserving
    /// aspect ratio, centred), and appends them to <paramref name="pdfDoc"/>.
    /// Supports <c>http/https</c> and <c>file://</c> URIs.
    /// </summary>
    private async Task EmbedPdfAsync(
        PdfDocument  pdfDoc,
        HttpClient   http,
        string?      inputUri,
        float        targetWidthPt,
        float        targetHeightPt,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(inputUri))
        {
            logger.LogWarning("pdf-type page has no input URI — skipping");
            return;
        }

        Stream? sourceStream = null;
        try
        {
            var uri = new Uri(inputUri);
            if (uri.Scheme is "http" or "https")
            {
                var bytes = await http.GetByteArrayAsync(inputUri, ct);
                sourceStream = new MemoryStream(bytes);
            }
            else if (uri.Scheme == "file")
            {
                sourceStream = File.OpenRead(uri.LocalPath);
            }
            else
            {
                logger.LogWarning(
                    "Unsupported scheme '{Scheme}' for pdf-type input {Uri} — skipping",
                    uri.Scheme, inputUri);
                return;
            }

            using var reader    = new PdfReader(sourceStream);
            using var sourceDoc = new PdfDocument(reader);

            for (var i = 1; i <= sourceDoc.GetNumberOfPages(); i++)
            {
                var srcPage = sourceDoc.GetPage(i);
                var srcRect = srcPage.GetPageSize();
                var xObj    = srcPage.CopyAsFormXObject(pdfDoc);
                var page    = pdfDoc.AddNewPage(new PageSize(targetWidthPt, targetHeightPt));

                // Scale to fit, preserving aspect ratio (letterbox/pillarbox if needed).
                var scale   = Math.Min(targetWidthPt  / srcRect.GetWidth(),
                                       targetHeightPt / srcRect.GetHeight());
                var offsetX = (targetWidthPt  - srcRect.GetWidth()  * scale) / 2f;
                var offsetY = (targetHeightPt - srcRect.GetHeight() * scale) / 2f;

                new PdfCanvas(page)
                    .AddXObjectWithTransformationMatrix(xObj, scale, 0, 0, scale, offsetX, offsetY)
                    .Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to embed PDF from {Uri} — skipping", inputUri);
        }
        finally
        {
            sourceStream?.Dispose();
        }
    }

    /// <summary>
    /// Fetches the first renderable image in <paramref name="entries"/> to determine the
    /// reference PDF page size (in points) that all non-image pages should match.
    /// Falls back to A4 if no image can be fetched.
    /// </summary>
    private async Task<(float widthPt, float heightPt)> DetermineReferenceSizeAsync(
        IReadOnlyList<PageSequenceEntry> entries,
        Dictionary<string, PageInfo>    manifestPages,
        HttpClient                      http,
        CancellationToken               ct)
    {
        foreach (var entry in entries)
        {
            if (entry.Type != null || entry.CanvasId == null) continue;
            if (!manifestPages.TryGetValue(entry.CanvasId, out var pageInfo)) continue;

            var url = ChooseImageUrl(pageInfo);
            try
            {
                var bytes     = await http.GetByteArrayAsync(url, ct);
                var imageData = ImageDataFactory.Create(bytes);
                return (
                    (float)(imageData.GetWidth()  * 72.0 / TargetDpi),
                    (float)(imageData.GetHeight() * 72.0 / TargetDpi));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Reference-size pre-fetch failed for {Url} — trying next", url);
            }
        }

        logger.LogDebug("No image page found for reference size — falling back to A4");
        return (PageSize.A4.GetWidth(), PageSize.A4.GetHeight());
    }

    // -------------------------------------------------------------------------
    // Image URL selection
    // -------------------------------------------------------------------------

    private static string ChooseImageUrl(PageInfo page)
    {
        var nativeMax = Math.Max(page.ImageWidth, page.ImageHeight);

        if (nativeMax <= MaxEdgePx || page.ServiceId == null)
            return page.StaticUrl;

        if (page.Sizes.Count > 0)
        {
            var best = page.Sizes
                .Where(s => Math.Max(s.Width, s.Height) <= MaxEdgePx)
                .OrderByDescending(s => Math.Max(s.Width, s.Height))
                .FirstOrDefault();

            if (best != null)
                return $"{page.ServiceId}/full/{best.Width},{best.Height}/0/default.jpg";
        }

        return $"{page.ServiceId}/full/!{MaxEdgePx},{MaxEdgePx}/0/default.jpg";
    }

    // -------------------------------------------------------------------------
    // Parsing helpers
    // -------------------------------------------------------------------------

    private static Dictionary<string, PageInfo> ParseManifestPages(string manifestJson)
    {
        var result = new Dictionary<string, PageInfo>(StringComparer.Ordinal);
        if (JsonNode.Parse(manifestJson) is not JsonObject manifest) return result;

        foreach (var item in manifest["items"] as JsonArray ?? [])
        {
            if (item is not JsonObject canvas) continue;

            var canvasId = canvas["id"]?.GetValue<string>() ?? canvas["@id"]?.GetValue<string>();
            if (canvasId == null) continue;

            var canvasWidth  = canvas["width"]?.GetValue<int>()  ?? 0;
            var canvasHeight = canvas["height"]?.GetValue<int>() ?? 0;

            var body = GetPaintingBody(canvas);
            if (body == null) continue;

            var bodyType = body["type"]?.GetValue<string>() ?? body["@type"]?.GetValue<string>() ?? "";
            if (!bodyType.Equals("Image", StringComparison.OrdinalIgnoreCase)) continue;

            var staticUrl = body["id"]?.GetValue<string>() ?? body["@id"]?.GetValue<string>();
            if (staticUrl == null) continue;

            var imageWidth  = body["width"]?.GetValue<int>()  ?? canvasWidth;
            var imageHeight = body["height"]?.GetValue<int>() ?? canvasHeight;

            string? serviceId = null;
            var sizes = new List<ImageSize>();

            var service = NormaliseToFirstObject(body["service"]);
            if (service != null)
            {
                serviceId = service["id"]?.GetValue<string>() ?? service["@id"]?.GetValue<string>();

                if (service["sizes"] is JsonArray sizesArr)
                {
                    foreach (var s in sizesArr)
                    {
                        var sw = s?["width"]?.GetValue<int>()  ?? 0;
                        var sh = s?["height"]?.GetValue<int>() ?? 0;
                        if (sw > 0 && sh > 0) sizes.Add(new ImageSize(sw, sh));
                    }
                }
            }

            result[canvasId] = new PageInfo(
                canvasId, canvasWidth, canvasHeight,
                staticUrl, imageWidth, imageHeight,
                serviceId, sizes);
        }

        return result;
    }

    private static List<PageSequenceEntry> ParsePageSequence(string json)
    {
        var entries = new List<PageSequenceEntry>();
        if (JsonNode.Parse(json) is not JsonObject root) return entries;

        foreach (var item in root["pages"] as JsonArray ?? [])
        {
            if (item is not JsonObject obj) continue;
            entries.Add(new PageSequenceEntry(
                Type:     obj["type"]?.GetValue<string>(),
                CanvasId: obj["canvasId"]?.GetValue<string>(),
                Input:    obj["input"]?.GetValue<string>(),
                Width:    obj["width"]?.GetValue<int>()   ?? 0,
                Height:   obj["height"]?.GetValue<int>()  ?? 0,
                Message:  obj["message"]?.GetValue<string>()));
        }

        return entries;
    }

    private static Dictionary<string, int> BuildCanvasIndexMap(Text text)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < text.Images.Length; i++)
            map[text.Images[i].ImageIdentifier] = i;
        return map;
    }

    private static JsonObject? GetPaintingBody(JsonObject canvas)
    {
        foreach (var annoPage in canvas["items"] as JsonArray ?? [])
        {
            if (annoPage is not JsonObject ap) continue;
            foreach (var anno in ap["items"] as JsonArray ?? [])
            {
                if (anno is not JsonObject a) continue;
                if (!IsPainting(a["motivation"])) continue;

                var body = a["body"];
                if (body is JsonArray arr) body = arr.FirstOrDefault();
                return body as JsonObject;
            }
        }
        return null;
    }

    private static bool IsPainting(JsonNode? motivation) => motivation switch
    {
        JsonValue  v   => v.TryGetValue<string>(out var s) && s.Equals("painting", StringComparison.OrdinalIgnoreCase),
        JsonArray  arr => arr.Any(m => m is JsonValue mv
                                    && mv.TryGetValue<string>(out var ms)
                                    && ms.Equals("painting", StringComparison.OrdinalIgnoreCase)),
        _              => false,
    };

    private static JsonObject? NormaliseToFirstObject(JsonNode? service) => service switch
    {
        JsonArray  arr => arr.FirstOrDefault() as JsonObject,
        JsonObject obj => obj,
        _              => null,
    };

    // -------------------------------------------------------------------------
    // Private types
    // -------------------------------------------------------------------------

    private sealed record PageSequenceEntry(
        string? Type,
        string? CanvasId,
        string? Input,
        int     Width,
        int     Height,
        string? Message);

    private sealed record PageInfo(
        string      CanvasId,
        int         CanvasWidth,
        int         CanvasHeight,
        string      StaticUrl,
        int         ImageWidth,
        int         ImageHeight,
        string?     ServiceId,
        List<ImageSize> Sizes);

    private sealed record ImageSize(int Width, int Height);

    /// <summary>
    /// Stream decorator that ignores <see cref="Close"/> and <see cref="Dispose(bool)"/>
    /// calls, preventing iText's <c>PdfWriter</c> from closing a stream it does not own.
    /// </summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead  => inner.CanRead;
        public override bool CanSeek  => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length   => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void  Flush() => inner.Flush();
        public override int   Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long  Seek(long offset, SeekOrigin origin)       => inner.Seek(offset, origin);
        public override void  SetLength(long value)                      => inner.SetLength(value);
        public override void  Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override void Close() { /* do not close the caller's stream */ }
        protected override void Dispose(bool disposing) { /* do not dispose the caller's stream */ }
    }
}
