using System.Text.Json.Nodes;
using MediatR;
using TextServices.Search.Api.Services;

namespace TextServices.Search.Api.Features.Annotations;

/// <summary>
/// Returns a IIIF Presentation 3 <c>AnnotationPage</c> with one annotation per text line
/// for a single canvas, using the IIIF Text Granularity extension
/// (<c>textGranularity: "line"</c>).
/// </summary>
public record LineAnnotationsRequest(string Id, int CanvasIndex, string SelfUrl)
    : IRequest<JsonObject?>;

public class LineAnnotationsHandler(ITextCache cache)
    : IRequestHandler<LineAnnotationsRequest, JsonObject?>
{
    public async Task<JsonObject?> Handle(LineAnnotationsRequest request, CancellationToken ct)
    {
        var text = await cache.GetTextAsync(request.Id, ct);
        if (text == null) return null;

        if (request.CanvasIndex < 0 || request.CanvasIndex >= text.Images.Length)
            return null;

        return AnnotationPageBuilder.Build(text, request.CanvasIndex, request.SelfUrl,
            wordLevel: false);
    }
}
