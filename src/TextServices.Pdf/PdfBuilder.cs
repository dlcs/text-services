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
/// </remarks>
public class PdfBuilder(IHttpClientFactory httpClientFactory, ILogger<PdfBuilder> logger)
{
    /// <summary>Named <see cref="HttpClient"/> used for fetching canvas images.</summary>
    public const string HttpClientName = "pdf";

    private const double TargetDpi  = 150.0;
    private const int    MaxEdgePx  = 2000;

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a PDF and writes it to <paramref name="output"/>.
    /// One page is emitted per entry in <paramref name="text"/>.Images; pages whose canvas has
    /// no matching manifest entry, or whose body is not an image, are silently skipped.
    /// Image fetch failures produce a blank page and are logged as warnings.
    /// </summary>
    public async Task BuildAsync(
        Text   text,
        string manifestJson,
        Stream output,
        CancellationToken ct)
    {
        var pages = ParseManifestPages(manifestJson);

        // Wrap output so that iText's PdfWriter.Close() cannot close the caller's stream.
        var pdfWriter = new PdfWriter(new NonClosingStream(output), new WriterProperties().SetFullCompressionMode(true));
        using var pdfDoc = new PdfDocument(pdfWriter);
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

        using var http = httpClientFactory.CreateClient(HttpClientName);

        for (var pageIndex = 0; pageIndex < text.Images.Length; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var image = text.Images[pageIndex];

            if (!pages.TryGetValue(image.ImageIdentifier, out var pageInfo))
            {
                logger.LogDebug(
                    "No manifest entry for canvas {CanvasId} — skipping page", image.ImageIdentifier);
                continue;
            }

            await AddPageAsync(pdfDoc, font, http, text, pageIndex, pageInfo, ct);
        }

        pdfDoc.Close();
    }

    // -------------------------------------------------------------------------
    // Page construction
    // -------------------------------------------------------------------------

    private async Task AddPageAsync(
        PdfDocument   pdfDoc,
        PdfFont       font,
        HttpClient    http,
        Text          text,
        int           pageIndex,
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
            pdfDoc.AddNewPage(new PageSize(
                (float)(pageInfo.CanvasWidth  * 72.0 / TargetDpi),
                (float)(pageInfo.CanvasHeight * 72.0 / TargetDpi)));
            return;
        }

        var imgWidthPx  = (int)imageData.GetWidth();
        var imgHeightPx = (int)imageData.GetHeight();

        var pageWidthPt  = (float)(imgWidthPx  * 72.0 / TargetDpi);
        var pageHeightPt = (float)(imgHeightPx * 72.0 / TargetDpi);

        var page   = pdfDoc.AddNewPage(new PageSize(pageWidthPt, pageHeightPt));
        var canvas = new PdfCanvas(page);

        // Background image — fills the entire page
        canvas.AddImageFittedIntoRectangle(
            imageData, new Rectangle(0, 0, pageWidthPt, pageHeightPt), false);

        // Invisible text layer
        var words = text.Words.Values
            .Where(w => w.Idx == pageIndex)
            .ToList();

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
                // PDF origin is bottom-left; flip Y axis
                var y           = pageHeightPt - (word.Y + word.H) * scaleY;
                var targetWidth = Math.Max(1f, word.W * scaleX);

                canvas.SetFontAndSize(font, fontSize);

                // Stretch word horizontally to fill its bounding box
                var naturalWidth = font.GetWidth(word.ContentRaw, fontSize);
                var hScale = naturalWidth > 0f ? targetWidth / naturalWidth * 100f : 100f;

                canvas.SetHorizontalScaling(hScale);
                canvas.SetTextMatrix(1, 0, 0, 1, x, y);
                canvas.ShowText(word.ContentRaw);
            }

            canvas.EndText();
        }

        canvas.Release();
    }

    // -------------------------------------------------------------------------
    // Image URL selection
    // -------------------------------------------------------------------------

    private static string ChooseImageUrl(PageInfo page)
    {
        var nativeMax = Math.Max(page.ImageWidth, page.ImageHeight);

        // Within limit or no service to scale with — use the static URL as-is
        if (nativeMax <= MaxEdgePx || page.ServiceId == null)
            return page.StaticUrl;

        // Service has pre-generated sizes — pick the largest that fits within the limit
        if (page.Sizes.Count > 0)
        {
            var best = page.Sizes
                .Where(s => Math.Max(s.Width, s.Height) <= MaxEdgePx)
                .OrderByDescending(s => Math.Max(s.Width, s.Height))
                .FirstOrDefault();

            if (best != null)
                return $"{page.ServiceId}/full/{best.Width},{best.Height}/0/default.jpg";
        }

        // Last resort: constrained IIIF Image API request
        return $"{page.ServiceId}/full/!{MaxEdgePx},{MaxEdgePx}/0/default.jpg";
    }

    // -------------------------------------------------------------------------
    // Manifest parsing
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

            // Skip non-image bodies (video, audio, etc.)
            var bodyType = body["type"]?.GetValue<string>() ?? body["@type"]?.GetValue<string>() ?? "";
            if (!bodyType.Equals("Image", StringComparison.OrdinalIgnoreCase)) continue;

            var staticUrl = body["id"]?.GetValue<string>() ?? body["@id"]?.GetValue<string>();
            if (staticUrl == null) continue;

            // Body's own dimensions preferred over canvas for the threshold check
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

    /// <summary>
    /// Walks the canvas annotation structure to find the body of the first painting annotation.
    /// <c>motivation</c> may be a single string or an array of strings per the IIIF spec.
    /// </summary>
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
                // body may be a single object or an array; take the first item
                if (body is JsonArray arr) body = arr.FirstOrDefault();
                return body as JsonObject;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="motivation"/> is the string "painting" or an array
    /// that contains "painting".
    /// </summary>
    private static bool IsPainting(JsonNode? motivation) => motivation switch
    {
        JsonValue  v   => v.TryGetValue<string>(out var s) && s.Equals("painting", StringComparison.OrdinalIgnoreCase),
        JsonArray  arr => arr.Any(m => m is JsonValue mv
                                    && mv.TryGetValue<string>(out var ms)
                                    && ms.Equals("painting", StringComparison.OrdinalIgnoreCase)),
        _              => false,
    };

    /// <summary>Normalises a service field that may be an object or an array to a single object.</summary>
    private static JsonObject? NormaliseToFirstObject(JsonNode? service) => service switch
    {
        JsonArray  arr => arr.FirstOrDefault() as JsonObject,
        JsonObject obj => obj,
        _              => null,
    };

    // -------------------------------------------------------------------------
    // Private types
    // -------------------------------------------------------------------------

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
    /// Stream decorator that forwards all operations to an inner stream but ignores
    /// <see cref="Close"/> and <see cref="Dispose(bool)"/> calls, preventing iText's
    /// <c>PdfWriter</c> from closing a stream it does not own.
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
