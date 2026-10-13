using System.Text.Json.Nodes;
using MediatR;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Svg;

/// <summary>
/// Returns an SVG document containing the line-level text of a single canvas, positioned to
/// overlay the canvas image. Linked from each canvas's <c>rendering</c> property in the
/// text-augmented Manifest.
/// </summary>
public record SvgRequest(string Id, int CanvasIndex) : IRequest<string?>;

public class SvgHandler(ITextStore textStore, ITextCache cache)
    : IRequestHandler<SvgRequest, string?>
{
    public async Task<string?> Handle(SvgRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Svg, ct)) return null;
        var text = await cache.GetTextAsync(request.Id, ct);
        if (text == null) return null;

        if (request.CanvasIndex < 0 || request.CanvasIndex >= text.Images.Length)
            return null;

        var image = text.Images[request.CanvasIndex];
        if (image.IsTemporalContent) return null;

        var (width, height) = image is { Width: > 0, Height: > 0 }
            ? (image.Width, image.Height)
            : await GetCanvasDimensionsFromManifest(request.Id, image.ImageIdentifier);
        if (width <= 0 || height <= 0) return null;

        return SvgBuilder.Build(text, request.CanvasIndex, width, height);
    }

    /// <summary>
    /// Fallback for text indexes built before canvas dimensions were recorded on
    /// <see cref="Core.Models.Image"/>: read them from the stored Manifest, if there is one.
    /// </summary>
    private async Task<(int Width, int Height)> GetCanvasDimensionsFromManifest(string id, string canvasId)
    {
        var json = await textStore.LoadManifest(id);
        if (json == null) return (0, 0);

        if (JsonNode.Parse(json)?["items"] is not JsonArray canvases) return (0, 0);

        var canvas = canvases.FirstOrDefault(c => c?["id"]?.GetValue<string>() == canvasId);
        if (canvas == null) return (0, 0);

        var width = canvas["width"]?.GetValue<int>() ?? 0;
        var height = canvas["height"]?.GetValue<int>() ?? 0;
        return (width, height);
    }
}
